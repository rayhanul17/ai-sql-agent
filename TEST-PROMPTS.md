# Test prompts — by intent

Copy-paste these to sanity-check the agent's behaviour. Every message is first
**classified** into one of four intents, then routed:

| Intent | Symbol | What happens |
|--------|--------|--------------|
| `DATA_QUERY`  | 📊 | writes + runs read-only SQL → table + summary (+ chart/Excel) |
| `SQL_GENERAL` | 📖 | answers as a schema-aware SQL tutor — explains, gives an example, but **runs nothing** |
| `META_HELP`   | 💬 | explains what the agent can do + suggests schema-grounded prompts |
| `OFF_TOPIC`   | 👋 | politely redirects back to the database — no query |

Tested against the seeded demo PostgreSQL DB
(tables: `students, teachers, classes, attendance, fees, leaves`).

> Behaviour scales with the model: Groq (qwen3-32b / llama-3.3-70b) and Ollama
> 7B are reliable; the small 3B handles the everyday cases but may slip on the
> trickier meta/edge ones. Reasoning models emit a `<think>` block that is
> stripped before the intent label / SQL is read.

---

## 1. Greetings / off-topic → 👏 no query (polite redirect)

```
hi
hello
thanks!
ke tumi
how are you
what's the weather
write me a poem about the sea
```

## 2. SQL concept / how-to (no real tables named) → 📖 no query (SQL tutor)

These teach a SQL concept using your actual schema for the example, but never
run a query:

```
what is a JOIN
how do I write a GROUP BY
difference between WHERE and HAVING
what is a primary key
how do I sort results in SQL
```

## 3. Meta / help / suggestions → 💬 no query (schema-aware reply)

```
what can you do
how do I use this
give me some example questions
suggest me a prompt to get helpful insight from the data
what should I ask
help me get insight from the data
```

## 4. Everyday data questions → 📊 query

```
how many students are there
how many teachers
list all students
top 3 teachers by salary
average teacher salary
students in grade 6
which students were absent
total fees collected
how many students in each class
customers per country            (on the MySQL classicmodels DB)
```

## 5. Schema / meta-about-the-DB questions → 📊 query

These ask *about* the database, so they read it (they are DATA_QUERY, not help):

```
what tables are there
how many rows in each table
list the columns of the students table
```

## 6. "Query" that names real tables → 📊 query (not a concept question)

Naming actual tables means the user wants real data, even if they say "query"
or "JOIN" — so these run, unlike the concept questions in section 2:

```
show me a JOIN of students and classes
count students grouped by class
join teachers with the classes they teach
```

## 7. Incomplete / informal / typo'd → 📊 query (the model fixes it)

```
get everything from student
student data
teachrs salary
show me student informations
SELECT * FROM student            (wrong table name → corrected to students)
SELCT * FRM studnts              (broken SQL → understood)
```

## 8. Language → answer matches the question

```
কতজন শিক্ষক আছে                 → Bangla answer 📊
koto jon teacher ache            → Bangla answer 📊 (Banglish understood)
how many teachers                → English answer 📊
```

Standing instruction (say it once, then ask normally):

```
ekhon theke banglay bolo         → then English questions still get Bangla answers
answer in english from now on    → switches back
```

## 9. Follow-ups (ask right after a query) → 📊 refine the previous query

```
as a chart
only their names
only males
sorted by salary
top 5
```

## 10. Dangerous / write / injection → ❌ refused (never executes)

These must be blocked by the safety layer:

```
delete all students
drop table teachers
update fees set amount = 0
truncate table orders
SELECT * FROM students; DROP TABLE teachers
insert into students values (...)
```

## 11. Edge cases

```
students in grade 99             → 📊 query, returns "no matching records"
show me the data                 → very vague; usually a helpful reply 💬
```

---

### What "correct" looks like

- Every message is classified first; the UI shows **"Generating SQL…"** only for
  data questions, and **"Thinking…"** for the conversational intents.
- Data questions → a **table** + a short summary + (when a label+number column
  exists) Bar/Line/Pie chart buttons + Excel export.
- SQL concept questions → a short tutor-style explanation with an example based on
  your real tables, and **no table/query run**.
- Meta/help → a short reply with 2-3 example questions grounded in your tables.
- Greetings/off-topic → a warm one-liner that steers back to the database.
- Dangerous requests → refused, nothing runs.
- The answer is in the same language as the question (or the language you last
  asked it to use).
