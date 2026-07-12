#!/usr/bin/env bash
#
# End-to-end smoke test for the AI SQL Agent.
#
# Drives the real /Chat/Ask SSE endpoint of a RUNNING app and checks that each
# kind of message takes the right branch:
#   - data questions run a query (a "rows" chunk is streamed);
#   - SQL-concept / meta / greeting messages reply WITHOUT running a query;
#   - follow-ups (with history) refine the previous query;
#   - Banglish is understood.
# If the demo Postgres container is reachable it also verifies the schema
# self-heal (a column renamed out-of-band is re-read and the query self-corrects)
# and dialect identifier quoting for a reserved-word column.
#
# The safety layer (SELECT-only, no DML/DDL, single statement) is covered by the
# unit tests, not here — a smoke test must never send destructive SQL to a DB.
#
# Usage:
#   scripts/e2e-smoke.sh                 # provider 1 (Groq), default host
#   PROVIDER=0 scripts/e2e-smoke.sh      # provider 0 (Ollama, local)
#   BASE_URL=http://localhost:5132 PROVIDER=1 scripts/e2e-smoke.sh
#   PG_CONTAINER=agent-postgres scripts/e2e-smoke.sh   # enable DB-mutating tests
#
# Requires: a running app, curl. DB tests additionally require docker + the
# demo Postgres container. Exits non-zero if any check fails.
set -u

BASE_URL="${BASE_URL:-http://localhost:5132}"
ASK="$BASE_URL/Chat/Ask"
PROVIDER="${PROVIDER:-1}"          # 1 = Groq (reliable), 0 = Ollama (local)
TIMEOUT="${TIMEOUT:-90}"
PG_CONTAINER="${PG_CONTAINER:-agent-postgres}"
PG_USER="${PG_USER:-agent_owner}"
PG_DB="${PG_DB:-agentdb}"

PASS=0; FAIL=0
red() { printf '\033[31m%s\033[0m' "$1"; }
grn() { printf '\033[32m%s\033[0m' "$1"; }

# ask <question> [history-json]  -> writes the raw SSE stream to $RESP
RESP=/tmp/e2e_resp.txt
ask() {
  local q="$1"; local hist="${2:-}"
  local extra=""
  [ -n "$hist" ] && extra=",\"history\":$hist"
  curl -sN -X POST "$ASK" -H "Content-Type: application/json" \
    -d "{\"question\":$(json_str "$q"),\"provider\":$PROVIDER$extra}" \
    --max-time "$TIMEOUT" 2>/dev/null > "$RESP"
}
json_str() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g' | awk '{printf "\"%s\"", $0}'; }

ran_query() { grep -q '"type":"rows"' "$RESP"; }
had_error() { grep -q '"type":"error"' "$RESP"; }
reply_text() { grep -o '"type":"token","content":"[^"]*"' "$RESP" \
  | sed 's/.*content":"//;s/"$//' | tr -d '\n' | sed 's/\\n/ /g' | head -c 90; }

# check <question> <expect: query|noquery> [history]
check() {
  local q="$1"; local expect="$2"; local hist="${3:-}"
  ask "$q" "$hist"
  local got="noquery"; ran_query && got="query"
  local ok=1
  [ "$got" = "$expect" ] || ok=0
  had_error && ok=0
  if [ $ok -eq 1 ]; then PASS=$((PASS+1)); printf '[%s] ' "$(grn PASS)";
  else FAIL=$((FAIL+1)); printf '[%s] ' "$(red FAIL)"; fi
  printf '%-46s expect=%-8s got=%-8s | %s\n' "$q" "$expect" "$got" "$(reply_text)"
}

echo "== AI SQL Agent e2e smoke =="
echo "   endpoint: $ASK   provider: $PROVIDER"
echo

echo "-- data questions (should run a query) --"
check "how many teachers are there"            query
check "list all students"                      query
check "average teacher salary"                 query

echo "-- SQL concept (tutor reply, no query) --"
check "what is a JOIN"                          noquery
check "how do I write a GROUP BY"              noquery

echo "-- meta / help (no query) --"
check "what can you do"                         noquery
check "suggest me a prompt for insight"        noquery

echo "-- greeting / off-topic (no query) --"
check "hi"                                      noquery
check "write me a poem about the sea"          noquery

echo "-- boundary: names real tables => data --"
check "show me a JOIN of students and classes" query
check "how many students are in each class"    query

echo "-- schema-meta (reads the DB => query) --"
check "what tables are there"                   query
check "how many rows in each table"             query

echo "-- language (Banglish understood) --"
check "koto jon teacher ache"                   query

echo "-- follow-ups (refine previous query) --"
HIST='[{"question":"how many students in each class","sql":"SELECT class_id, COUNT(*) FROM students GROUP BY class_id"}]'
check "as a chart"                              query "$HIST"
check "only top 3"                              query "$HIST"

# ---- Optional DB-mutating tests (need the demo Postgres container) ----
if command -v docker >/dev/null 2>&1 && \
   docker exec "$PG_CONTAINER" pg_isready -U "$PG_USER" -d "$PG_DB" >/dev/null 2>&1; then
  psql() { docker exec "$PG_CONTAINER" psql -U "$PG_USER" -d "$PG_DB" "$@"; }

  echo
  echo "-- schema self-heal (rename a column out-of-band) --"
  ask "average teacher salary" >/dev/null                       # warm the cache
  psql -c 'ALTER TABLE teachers RENAME COLUMN salary TO monthly_salary;' >/dev/null 2>&1
  ask "average teacher salary"
  if ran_query && ! had_error && grep -q "Refreshing schema" "$RESP"; then
    PASS=$((PASS+1)); printf '[%s] self-heal recovered after column rename | %s\n' "$(grn PASS)" "$(reply_text)"
  else
    FAIL=$((FAIL+1)); printf '[%s] self-heal did NOT recover\n' "$(red FAIL)"
  fi
  psql -c 'ALTER TABLE teachers RENAME COLUMN monthly_salary TO salary;' >/dev/null 2>&1  # restore

  echo "-- dialect quoting (reserved-word column) --"
  psql -c 'ALTER TABLE students ADD COLUMN "order" int DEFAULT 1;' >/dev/null 2>&1
  curl -s -X POST "$BASE_URL/Chat/LoadSchema" -H "Content-Type: application/json" \
    -d '{}' --max-time 30 >/dev/null 2>&1                        # refresh so cache sees it
  ask "list students sorted by their order column, show name and order"
  if ran_query && ! had_error; then
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
