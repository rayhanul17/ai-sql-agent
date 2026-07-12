# Architecture — from prompt to response

A high-level walkthrough of what happens to one user message, from the moment it
leaves the browser until the answer finishes streaming back. File/line pointers
are given so you can open the code at each hop.

---

## The big picture

```
┌──────────────┐   POST /Chat/Ask (SSE)   ┌───────────────────┐
│   Browser    │ ───────────────────────▶ │   ChatController   │
│  (chat.js)   │ ◀─────────────────────── │      .Ask()        │
└──────────────┘   data: {chunk}\n\n      └─────────┬─────────┘
       ▲   reads chunks, updates UI                 │ await foreach
       │                                            ▼
       │                              ┌──────────────────────────────┐
       │                              │      QueryAgentService        │
       │      status / sql /          │       .AskStreamAsync()       │
       └── rows / token / done ◀────── │  (orchestrator, yields each   │
                                       │   step as a StreamChunk)      │
                                       └──────────────┬───────────────┘
                                                      │
        ┌─────────────────────────────────────────────┼───────────────────────────────┐
        ▼                     ▼                        ▼                ▼               ▼
  SchemaIntrospector    PromptBuilder            IAiProvider      SqlSafety         SqlExecutor
  (+ SchemaCache)       (builds prompts)         (Ollama/Groq     Validator         (READ ONLY txn)
                                                  via Semantic     (SELECT-only)
                                                  Kernel)
```

**Everything is streamed.** The orchestrator doesn't compute the whole answer and
return it — it `yield return`s each step (`status`, then `sql`, then `rows`, then
`token`…`token`, then `done`) the instant it's ready, and the controller pushes
each one to the browser as a Server-Sent Event. That's why the UI shows live
progress ("Reading schema…", "Generating SQL…") and the answer types itself out.

---

## Step by step (mainline: a data question)

### 1. Browser sends the prompt
[`chat.js` › `ask()`](../src/SqlAgent.Web/wwwroot/js/chat.js) — `fetch('/Chat/Ask', POST)`
with `{ question, provider, model, connectionString, dialect, history }`. It uses
`fetch` + a stream reader (not `EventSource`, because the request is a POST) and
reads the response body chunk by chunk. The **applied** (saved) settings are sent,
not whatever is currently typed in the panel.

### 2. Controller opens the SSE stream
[`ChatController.Ask()`](../src/SqlAgent.Web/Controllers/ChatController.cs) — sets
`Content-Type: text/event-stream`, maps the DTO to an `AskRequest`, then
`await foreach`es over the orchestrator, serializing each `StreamChunk` as
`data: {json}\n\n` and flushing immediately.

### 3. Orchestrator runs the pipeline
[`QueryAgentService.AskStreamAsync()`](../src/SqlAgent.Application/Services/QueryAgentService.cs):

| # | Step | UI status | Calls |
|---|------|-----------|-------|
| a | Resolve connection + dialect + provider + model | — | `Resolve()`, `ISqlDialectFactory` |
| b | Read the schema (cached) | `Reading database schema…` | `ISchemaIntrospector` + `ISchemaCache` |
| c | **Classify the intent** | `Understanding your question…` | `PromptBuilder.BuildClassifyPrompt` → `IAiProvider` |
| d | Branch on intent | — | see below |
| e | Generate SQL *(data only)* | `Generating SQL…` | `PromptBuilder.BuildSqlPrompt` → `IAiProvider` |
| f | Validate SQL *(data only)* | — | `ISqlSafetyValidator` |
| g | Run it read-only *(data only)* | `Running query (read-only)…` | `ISqlExecutor` |
| h | Explain the result *(data only)* | `Explaining result…` | `PromptBuilder.BuildExplanationPrompt` → `IAiProvider` (streamed) |
| i | Done | — | emits a `done` chunk |

### 4. The intent branch (step d)

The message is classified into one of **five** intents, and only **data**
questions touch the database:

```
        ┌──────────────────────────────────────────────────────────┐
        │  analyse → INTENT + LANGUAGE  (a message can be BOTH a      │
        │  data query AND a language instruction). A fast regex       │
        │  short-circuits a PURE language instruction; otherwise      │
        │  one small LLM call returns two lines the code parses.      │
        └───────────────────────┬──────────────────────────────────┘
                                │  INTENT drives the branch;
                                │  LANGUAGE (if any) is applied to the answer
   ┌───────────┬───────────┬────┴──────┬───────────┬───────────────┐
   ▼           ▼           ▼           ▼           ▼
DATA_QUERY  SQL_GENERAL  META_HELP  INSTRUCTION  OFF_TOPIC
write+run   SQL tutor    help +     ack a        polite
SQL →       reply        example    behaviour/   redirect
table+chart (no query)   prompts    language     (no query)
status:     Thinking…    (no query) instruction  Thinking…
Generating                          (no query)
SQL…
```

A data question falls through to steps e–i. The other four call
`NonDataPrompt()` to pick the right conversational prompt, stream the reply as
`token` chunks, and stop — no SQL is generated or run.

**Unanswerable guard.** Even on the data path, the SQL step can decide the
question's subject isn't in this schema (e.g. "which sales rep brought the most
revenue" against a students/teachers DB). Rather than force a hallucinated join
onto unrelated tables, the model returns the token `NO_DATA`; the code catches it
and replies with what the database *does* contain plus an example — no query
runs. It's schema-aware, not keyword-based: the same question runs normally on a
DB that has employees/payments, and close synonyms ("pupils" → students) still
generate SQL.

**Why analyse, not just classify.** A single message can carry more than one
thing — most importantly a data request *and* a language instruction together
("how many tables, answer in Bangla"). A single-label classifier would pick one
and drop the other (it used to mislabel that whole message as an INSTRUCTION and
run no query). So the analysis returns TWO fields: the primary `INTENT` and any
requested reply `LANGUAGE`. The code then decides — a data query still runs, and
the requested language is applied to the answer.

`INSTRUCTION` is only for a message whose *sole* point is to set behaviour/
language ("from now on answer in English"). Because a tiny local model can
mistake that for a follow-up query, a deterministic regex
(`LooksLikeLanguageInstruction`) short-circuits the *pure* case *before* the LLM
call — but it stands down if the message also asks for data (`LooksLikeDataRequest`),
so a double prompt is never swallowed. Anything the regex doesn't catch falls
through to the LLM, which returns the `INTENT`/`LANGUAGE` lines.

### 5. Self-correction (data path)

If the query fails (e.g. a column that changed since the schema was cached), the
orchestrator invalidates the cache, **re-reads the schema fresh** (`Refreshing
schema…`), feeds the DB error into `BuildRetryPrompt`, and retries once. This is
how a stale-schema failure self-heals without the user seeing an error.

### 6. Reply streams back
Each token from the explanation flows: provider → `ThinkFilter` (strips a
reasoning model's `<think>…</think>`) → `StreamChunk{type:"token"}` → SSE →
[`chat.js`](../src/SqlAgent.Web/wwwroot/js/chat.js) appends it to the answer,
producing the typing effect. `sql` chunks fill the "Show query" modal, `rows`
render the table + chart + Excel buttons, `done` finalizes and records the turn
in history.

**SSE event order for a data query:**
`status → status → status(Generating SQL) → sql → status(Running) → rows → status(Explaining) → token × N → done`

---

## Conversation context (history)

Follow-ups like "as a chart" or "only the top 3" need to know the previous query,
so a bounded slice of history rides along with each request — kept deliberately
small so the context never grows without limit:

- The client stores one entry per successful data turn: just `{question, sql}` —
  **not** the answer text or the result rows. On the next request it sends only
  the last **4** turns (`MAX_HISTORY` in `chat.js`).
- The server renders those into a short "Recent conversation" block in the prompt
  (`RenderHistory` in `PromptBuilder`) — each line is `User asked: …` + `SQL
  used: …`. That's enough for the model to resolve a follow-up against the prior
  query.
- The **schema is not part of history** — it's introspected/cached separately and
  injected fresh each turn, so it can't go stale inside the conversation.

Why this stays well inside every model's context window: 4 turns × (a question +
one SQL line) is a few hundred tokens at most, versus ~32K tokens for the local
3B and ~128K for the Groq model. Sending whole answers or result tables would
balloon the prompt for no benefit — the SQL alone is what a follow-up needs.

---

## Where Semantic Kernel fits

**Semantic Kernel (SK)** is Microsoft's .NET SDK that gives a single, uniform way
to talk to any chat LLM. In this project it lives entirely behind the
`IAiProvider` interface, so the rest of the app never knows whether it's talking
to a local or a cloud model.

- **Ollama** (local): [`OllamaAiProvider`](../src/SqlAgent.Infrastructure/Ai/OllamaAiProvider.cs)
  builds an `OllamaApiClient` and adapts it to SK's `IChatCompletionService` via
  `AsChatCompletionService()`.
- **Groq** (cloud): [`GroqAiProvider`](../src/SqlAgent.Infrastructure/Ai/GroqAiProvider.cs)
  uses SK's OpenAI connector (`OpenAIChatCompletionService`) pointed at Groq's
  OpenAI-compatible endpoint.

Both call the **same** two SK methods:

| Purpose | SK method | Used for |
|---------|-----------|----------|
| One-shot answer | `GetChatMessageContentAsync` | classify (step c), generate SQL (step e) |
| Token stream | `GetStreamingChatMessageContentsAsync` | explanation + conversational replies (steps d, h) |

So SK is doing exactly one job — **being the model client** — at steps c, e, h and
the non-data branch. Everything else (schema reading, safety, execution, the SSE
plumbing) is the app's own code. Because both providers expose the same interface,
switching Ollama ⇄ Groq at runtime is just choosing which `IAiProvider` to
resolve; no other code changes.

> Note: we intentionally use only SK's lightweight chat-client surface, not its
> `Kernel` / plugins / DI registration — the chat client is built per call so the
> model can change per request.

---

## Safety, in layers (defense in depth)

The LLM is never trusted to be safe on its own:

1. **Classify** keeps non-data messages away from SQL generation entirely.
2. [`SqlSafetyValidator`](../src/SqlAgent.Application/Services/SqlSafetyValidator.cs)
   strips model formatting, keeps only the first statement (so `…; DROP TABLE` is
   discarded), requires a leading `SELECT`/`WITH`, and blocks any write/DDL keyword.
3. [`SqlExecutor`](../src/SqlAgent.Infrastructure/Database/SqlExecutor.cs) runs the
   query inside a **READ ONLY transaction** with a statement timeout.
4. The recommended connection string uses a **read-only DB user** as the final
   backstop.

Even if classification or the model misbehaves, layers 2–4 guarantee nothing but a
read-only `SELECT` ever reaches the database.
