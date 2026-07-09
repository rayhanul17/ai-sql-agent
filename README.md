# AI SQL Agent — Chat with your Database

A local, privacy-friendly AI assistant that turns natural-language questions
into **safe, read-only SQL**, runs them against a relational database, and
streams back a plain-language answer — with the generated SQL, an HTML result
table, an Excel export and an optional chart.

Built with **.NET 10 + ASP.NET Core MVC**, **Microsoft Semantic Kernel**
(the .NET equivalent of LangChain) and a **local LLM via Ollama**
(`qwen2.5-coder`). No cloud, no API keys.

---

## What it does

```
Question (plain English/Bangla)
        ↓
ASP.NET Core MVC  ──►  Semantic Kernel  ──►  Ollama (Qwen 2.5 Coder)
        ↓                                         (generates SQL)
SQL Safety Layer  (read-only, single SELECT, row limit)
        ↓
PostgreSQL / MySQL / SQL Server   (READ ONLY transaction)
        ↓
HTML table + Show-Query modal + Excel + Chart + streamed explanation
```

Ask things like:
- *"Which students were absent this month?"*
- *"Top 5 teachers by salary."*
- *"How many fees are still unpaid for July?"*

---

## Key features

| Area | Detail |
|------|--------|
| **AI orchestration** | Microsoft Semantic Kernel over Ollama's `IChatClient` |
| **Local LLM** | `qwen2.5-coder` — runtime-switchable model dropdown (3B / 14B) |
| **Model loader** | Warm-up call + `load_duration` tracking → live "Loading model…" state |
| **SQL safety** | Defense in depth (see below) |
| **Multi-dialect** | PostgreSQL, MySQL, SQL Server via an `ISqlDialect` abstraction |
| **Runtime data source** | Paste any connection string in the UI, or use the seeded demo DB |
| **Streaming** | Answer streamed token-by-token over Server-Sent Events (SSE) |
| **Results UI** | Responsive chat, HTML table, **Show Query** modal, **Excel** export (ClosedXML), **Chart.js** graphs |

---

## SQL safety (defense in depth)

The LLM is **never trusted**. A generated query must pass every layer:

1. **Cleaned** — markdown fences / labels stripped from the model output.
2. **Single statement** — `;`-chained payloads rejected.
3. **SELECT-only** — must start with `SELECT` (or a `WITH … SELECT` CTE).
4. **Keyword blocklist** — `INSERT/UPDATE/DELETE/DROP/ALTER/TRUNCATE/CREATE/GRANT/EXEC/…` rejected.
5. **Forced row limit** — a dialect-aware `LIMIT`/`TOP` is injected if missing.
6. **READ ONLY transaction** — executed read-only; the DB refuses writes.
7. **Read-only DB user** — the demo DB connects as a `SELECT`-only role.
8. **Statement timeout** — heavy queries are killed.
9. **Audit log** — every generated SQL is logged (Serilog).

---

## Architecture (Clean Architecture, no MediatR)

```
src/
  SqlAgent.Domain          contracts + models (no dependencies)
  SqlAgent.Application      PromptBuilder, SqlSafetyValidator, QueryAgentService
  SqlAgent.Infrastructure   Ollama (Semantic Kernel), DB access, dialects
  SqlAgent.Web              ASP.NET Core MVC (controller, views, SSE, Excel)
docker-compose.yml          PostgreSQL (seeded) — Ollama runs natively
```

> MediatR/CQRS was intentionally omitted: this is a single, clear pipeline,
> and MediatR's request/response model is awkward for streaming. The layers
> keep the app testable and let the AI provider / DB dialect be swapped freely.

---

## Getting started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Docker Desktop](https://www.docker.com/)

### Two ways to run

**A) Development** — infra in Docker, app runs natively (fast to iterate):

```bash
# Starts PostgreSQL (seeded) + Ollama, and auto-pulls BOTH models
# (qwen2.5-coder:3b and :7b). First run downloads ~6 GB — give it a moment.
docker compose up -d

# Run the web app:
dotnet run --project src/SqlAgent.Web
```

`docker compose up` creates `agentdb`, seeds the demo schema (students,
teachers, attendance, fees, leaves) and a read-only role `agent_readonly`.
Only pulled models appear (enabled) in the UI dropdown; 3B is the default.

**B) All-in-one (final test / production)** — everything, including the app,
in containers:

```bash
docker compose -f docker-compose.full.yml up
```
Then open the app's mapped port. (Requires enough RAM to hold the model —
3B is the safe default.)

---

## Using your own database

In the sidebar, pick **PostgreSQL / MySQL / SQL Server (custom)** and paste a
connection string. The agent introspects that database's live schema and
answers against it. Prefer a **read-only** connection string — the app enforces
read-only, but a least-privileged user is the safest backstop.

---

## Model strategy

One family (**Qwen 2.5 Coder**) keeps prompting consistent while scaling by size:

| Tier | Model | Approx RAM | For |
|------|-------|-----------|-----|
| Minimum | `qwen2.5-coder:3b` | ~3 GB | modest machines (default) |
| Better | `qwen2.5-coder:7b` | ~6 GB | better SQL, 16 GB RAM |

Switching models in the UI triggers a warm-up; the first request after a
cold switch loads the model into RAM (a few seconds for larger models), then
subsequent queries are fast. A larger tier (e.g. 14B) or a cloud LLM can be
added later by one config line.

---

## Future improvements
- Add a larger tier (14B) and optional cloud LLM (OpenAI/Claude) via the same `IAiProvider`.
- Query-result caching (Redis) for repeated questions.
- Role-based data access (row-level restrictions per user).
- Few-shot examples in the prompt to improve SQL accuracy.
- NoSQL support (e.g. MongoDB) via a parallel `IDataSourceAgent` — kept out of
  v1 to stay focused on Text-to-SQL.

---

## Tech stack
.NET 10 · ASP.NET Core MVC · Semantic Kernel · Ollama (Qwen 2.5 Coder) ·
PostgreSQL / MySQL / SQL Server · Npgsql · MySqlConnector · Microsoft.Data.SqlClient ·
ClosedXML · Chart.js · Bootstrap 5 · Serilog · Docker Compose
