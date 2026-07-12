#!/usr/bin/env bash
#
# End-to-end smoke test for the AI SQL Agent.
#
# Drives the real /Chat/Ask SSE endpoint of a RUNNING app and checks that each
# kind of message takes the right branch:
#   - data questions run a query (a "rows" chunk is streamed);
#   - SQL-concept / meta / greeting / instruction messages reply WITHOUT a query;
#   - follow-ups (with history) refine the previous query;
#   - Banglish is understood.
# If the demo Postgres container is reachable it also verifies the schema
# self-heal (a column renamed out-of-band is re-read and the query self-corrects)
# and dialect identifier quoting for a reserved-word column.
#
# The intent checks are INDEPENDENT, so they run CONCURRENTLY (each is its own
# LLM call) and results are collected and printed in order — turning ~20 serial
# calls into roughly one call's wall-clock. The DB-mutating tests stay sequential
# (they alter and restore a column). The safety layer (SELECT-only, no DML/DDL)
# is covered by unit tests, not here — a smoke test never sends destructive SQL.
#
# Usage:
#   scripts/e2e-smoke.sh                 # provider 1 (Groq), default host
#   PROVIDER=0 scripts/e2e-smoke.sh      # provider 0 (Ollama, local)
#   BASE_URL=http://localhost:5132 PROVIDER=1 scripts/e2e-smoke.sh
#   PG_CONTAINER=agent-postgres scripts/e2e-smoke.sh   # enable DB-mutating tests
#
# Requires: a running app, curl. DB tests additionally require docker + the demo
# Postgres container. Exits non-zero if any check fails.
set -u

BASE_URL="${BASE_URL:-http://localhost:5132}"
ASK="$BASE_URL/Chat/Ask"
PROVIDER="${PROVIDER:-1}"          # 1 = Groq (reliable), 0 = Ollama (local)
TIMEOUT="${TIMEOUT:-90}"
# How many checks to run at once (default 1 = sequential).
# IMPORTANT: Groq's free tier has a low tokens-per-minute limit (~6000 TPM), and
# each check spends classify+generate+explain tokens. Running several at once
# bunches that spend into the same minute and trips HTTP 429, which comes back as
# empty/truncated responses (false failures). So the safe default is sequential;
# raise CONCURRENCY only against a provider/tier with real TPM headroom
# (e.g. CONCURRENCY=4 on a paid Groq tier or a beefy local Ollama).
CONCURRENCY="${CONCURRENCY:-1}"
PG_CONTAINER="${PG_CONTAINER:-agent-postgres}"
PG_USER="${PG_USER:-agent_owner}"
PG_DB="${PG_DB:-agentdb}"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
red() { printf '\033[31m%s\033[0m' "$1"; }
grn() { printf '\033[32m%s\033[0m' "$1"; }

json_str() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g' | awk '{printf "\"%s\"", $0}'; }

# Fire one /Chat/Ask and write the SSE stream to the given file. Retries once
# after a short pause if the stream ends in an error or produced no answer — this
# absorbs a transient provider rate-limit (HTTP 429) so it isn't a false failure.
ask_to() {
  local out="$1"; local q="$2"; local hist="${3:-}"
  local extra=""
  [ -n "$hist" ] && extra=",\"history\":$hist"
  local attempt
  for attempt in 1 2; do
    curl -sN -X POST "$ASK" -H "Content-Type: application/json" \
      -d "{\"question\":$(json_str "$q"),\"provider\":$PROVIDER$extra}" \
      --max-time "$TIMEOUT" 2>/dev/null > "$out"
    # Good enough if we got a token or rows and no error chunk.
    if grep -q '"type":"\(token\|rows\)"' "$out" && ! grep -q '"type":"error"' "$out"; then
      return 0
    fi
    [ "$attempt" = 1 ] && sleep 15   # wait out a per-minute token limit
  done
}

# ---- Concurrent intent checks -------------------------------------------------
# Each check is registered with an index so output prints in declaration order.
declare -a C_Q C_EXPECT C_HIST C_SECTION
IDX=0
reg() { # reg <question> <expect> [history] [section-header]
  C_Q[$IDX]="$1"; C_EXPECT[$IDX]="$2"; C_HIST[$IDX]="${3:-}"; C_SECTION[$IDX]="${4:-}"
  IDX=$((IDX+1))
}

reg "how many teachers are there"            query   "" "-- data questions (should run a query) --"
reg "list all students"                      query
reg "average teacher salary"                 query
reg "what is a JOIN"                          noquery "" "-- SQL concept (tutor reply, no query) --"
reg "how do I write a GROUP BY"              noquery
reg "what can you do"                         noquery "" "-- meta / help (no query) --"
reg "suggest me a prompt for insight"        noquery
reg "hi"                                      noquery "" "-- greeting / off-topic (no query) --"
reg "write me a poem about the sea"          noquery
reg "from now on answer in English"          noquery "" "-- instruction: language/behaviour (no query) --"
reg "always keep your answers short"         noquery
reg "show me a JOIN of students and classes" query   "" "-- boundary: names real tables => data --"
reg "how many students are in each class"    query
reg "what tables are there"                   query  "" "-- schema-meta (reads the DB => query) --"
reg "how many rows in each table"             query
reg "koto jon teacher ache"                   query  "" "-- language (Banglish understood) --"
HIST='[{"question":"how many students in each class","sql":"SELECT class_id, COUNT(*) FROM students GROUP BY class_id"}]'
reg "as a chart"                              query   "$HIST" "-- follow-ups (refine previous query) --"
reg "only top 3"                              query   "$HIST"

echo "== AI SQL Agent e2e smoke =="
echo "   endpoint: $ASK   provider: $PROVIDER   ($IDX checks, up to $CONCURRENCY at a time)"
echo

# Launch checks in the background but cap how many run at once: before starting
# a new one, wait while the number of running jobs is at the limit.
for i in $(seq 0 $((IDX-1))); do
  while [ "$(jobs -rp | wc -l)" -ge "$CONCURRENCY" ]; do wait -n 2>/dev/null || sleep 0.2; done
  ask_to "$WORK/resp_$i.txt" "${C_Q[$i]}" "${C_HIST[$i]}" &
done
wait   # all intent checks done

PASS=0; FAIL=0
for i in $(seq 0 $((IDX-1))); do
  [ -n "${C_SECTION[$i]}" ] && { echo; echo "${C_SECTION[$i]}"; }
  resp="$WORK/resp_$i.txt"
  got="noquery"; grep -q '"type":"rows"' "$resp" && got="query"
  err="no"; grep -q '"type":"error"' "$resp" && err="yes"
  reply=$(grep -o '"type":"token","content":"[^"]*"' "$resp" \
    | sed 's/.*content":"//;s/"$//' | tr -d '\n' | sed 's/\\n/ /g' | head -c 80)
  if [ "$got" = "${C_EXPECT[$i]}" ] && [ "$err" = "no" ]; then
    PASS=$((PASS+1)); mark="$(grn PASS)"
  else
    FAIL=$((FAIL+1)); mark="$(red FAIL)"
  fi
  printf '[%s] %-46s expect=%-8s got=%-8s | %s\n' "$mark" "${C_Q[$i]}" "${C_EXPECT[$i]}" "$got" "$reply"
done

# ---- Optional DB-mutating tests (sequential — they alter/restore a column) ----
if command -v docker >/dev/null 2>&1 && \
   docker exec "$PG_CONTAINER" pg_isready -U "$PG_USER" -d "$PG_DB" >/dev/null 2>&1; then
  psql() { docker exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" "$@"; }
  R="$WORK/db.txt"

  echo
  echo "-- schema self-heal (rename a column out-of-band) --"
  ask_to "$R" "average teacher salary" >/dev/null                 # warm the cache
  psql -c 'ALTER TABLE teachers RENAME COLUMN salary TO monthly_salary;' >/dev/null 2>&1
  ask_to "$R" "average teacher salary"
  if grep -q '"type":"rows"' "$R" && ! grep -q '"type":"error"' "$R" && grep -q "Refreshing schema" "$R"; then
    PASS=$((PASS+1)); printf '[%s] self-heal recovered after column rename\n' "$(grn PASS)"
  else
    FAIL=$((FAIL+1)); printf '[%s] self-heal did NOT recover\n' "$(red FAIL)"
  fi
  psql -c 'ALTER TABLE teachers RENAME COLUMN monthly_salary TO salary;' >/dev/null 2>&1  # restore

  echo "-- dialect quoting (reserved-word column) --"
  psql -c 'ALTER TABLE students ADD COLUMN "order" int DEFAULT 1;' >/dev/null 2>&1
  curl -s -X POST "$BASE_URL/Chat/LoadSchema" -H "Content-Type: application/json" \
    -d '{}' --max-time 30 >/dev/null 2>&1                          # refresh so cache sees it
  ask_to "$R" "list students sorted by their order column, show name and order"
  if grep -q '"type":"rows"' "$R" && ! grep -q '"type":"error"' "$R"; then
    PASS=$((PASS+1)); printf '[%s] reserved-word column query ran without error\n' "$(grn PASS)"
  else
    FAIL=$((FAIL+1)); printf '[%s] reserved-word column query failed\n' "$(red FAIL)"
  fi
  psql -c 'ALTER TABLE students DROP COLUMN "order";' >/dev/null 2>&1  # cleanup
else
  echo
  echo "(skipping DB self-heal / quoting tests — container '$PG_CONTAINER' not reachable)"
fi

echo
echo "==============================="
printf 'RESULT: %s passed, %s failed\n' "$(grn "$PASS")" "$([ "$FAIL" -eq 0 ] && grn 0 || red "$FAIL")"
[ "$FAIL" -eq 0 ]
