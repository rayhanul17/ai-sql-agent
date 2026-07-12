using System.Text;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Application.Services;

/// <summary>
/// Builds the two prompts the agent needs:
///  1) a schema-grounded, dialect-aware prompt that asks the LLM for ONE
///     read-only SELECT and nothing else;
///  2) an explanation prompt that turns the returned rows into a short answer.
/// Pure/text-only — no LLM or DB calls, so it is easy to reason about.
/// </summary>
public sealed class PromptBuilder
{
    /// <summary>Render the schema compactly, e.g. Students(Id int PK, Name text, ClassId int -> Classes.Id).</summary>
    public string RenderSchema(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        foreach (var table in schema.Tables)
        {
            sb.Append(table.Name).Append('(');
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var c = table.Columns[i];
                if (i > 0) sb.Append(", ");
                sb.Append(c.Name).Append(' ').Append(c.DataType);
                if (c.IsPrimaryKey) sb.Append(" PK");
                if (c.ForeignKeyReference is not null)
                    sb.Append(" -> ").Append(c.ForeignKeyReference);
            }
            sb.AppendLine(")");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Analyse the user's message up front. A message can carry more than one
    /// thing — most importantly a data request AND a language instruction together
    /// ("how many tables, answer in Bangla") — so instead of a single label this
    /// returns TWO lines: the primary INTENT, and any requested reply LANGUAGE.
    /// The output is line-based (not JSON) so even a tiny model parses reliably.
    /// </summary>
    public string BuildAnalyzePrompt(
        string question, DatabaseSchema schema, IReadOnlyList<ConversationTurn>? history = null)
    {
        var tableList = string.Join(", ", schema.Tables.Select(t => t.Name));
        var historyText = RenderHistory(history);
        return $"""
            You analyse a user's message to an AI SQL agent that answers questions
            about a database. The database has these tables: {tableList}
            {historyText}
            The message may be in English, Bangla, or Banglish (Bangla in Latin
            letters) — understand it either way.

            Reply with EXACTLY these two lines and nothing else:
            INTENT: <one label from the list below>
            LANGUAGE: <english | bangla | banglish | none>

            For INTENT, pick the PRIMARY thing the message asks for:

            DATA_QUERY  — asks for specific rows/values/counts FROM the tables, or a
                          question about the database itself — its tables, columns,
                          or row counts. "what tables are there", "list the columns
                          of X", "how many rows in each table" are all DATA_QUERY
                          (they read the database), NOT help requests. ALSO: a short
                          follow-up that refines the PREVIOUS data query (e.g. "as a
                          chart", "only males", "sorted by salary", "top 5").
                          IMPORTANT: if the message names ACTUAL tables/columns (even
                          while asking for a "query"/"JOIN"), it wants real data ->
                          DATA_QUERY. E.g. "show me a JOIN of students and classes".
                          CRUCIAL: if the message asks for data AND ALSO says which
                          language to answer in (e.g. "how many tables, answer in
                          Bangla"), the INTENT is still DATA_QUERY — the language part
                          goes in the LANGUAGE line, it does NOT make it an
                          instruction.

            SQL_GENERAL — a general SQL/DB CONCEPT or how-to question with NO
                          reference to this database's actual tables. Examples: "what
                          is a JOIN", "how do I write a GROUP BY", "what is a primary
                          key". (If it names real tables, it is DATA_QUERY instead.)

            META_HELP   — asks what the agent can do, how to use it, or for prompt/
                          question IDEAS. Examples: "what can you do", "suggest a
                          prompt", "what should I ask", "help me get insight".

            INSTRUCTION — the message's ONLY point is to tell the agent HOW to behave/
                          reply, with NO data request. Most often a pure language
                          preference. Examples: "from now on answer in English",
                          "ekhon theke banglay bolo", "reply in Bangla", "always keep
                          answers short". If the message ALSO asks for data, it is
                          DATA_QUERY (not INSTRUCTION) — see the CRUCIAL note above.

            OFF_TOPIC   — greeting, thanks, small talk, or anything unrelated to the
                          database or SQL. Examples: "hi", "thanks", "write me a poem".

            For LANGUAGE, output the language the user asks THIS reply to be in, if
            any ("in Bangla", "answer in English", "banglay bolo" -> bangla/english).
            If the message does not ask for a specific language, output: none.

            Message: {question}

            INTENT:
            """;
    }

    public string BuildSqlPrompt(
        string question, DatabaseSchema schema, ISqlDialect dialect,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var schemaText = RenderSchema(schema);
        var historyText = RenderHistory(history);
        return $"""
            You are a senior data analyst that writes {dialect.DisplayName} SQL.

            Database schema (only these tables/columns exist):
            {schemaText}
            {historyText}
            The question may be in English, Bangla, or Banglish (Bangla written in
            Latin letters) — understand it either way and answer it with SQL.

            Rules:
            - Write exactly ONE read-only SELECT for the question. Questions about
              the database itself (tables, columns, counts, rows per table) count too.
            - For "what tables are there", do NOT use information_schema. The table
              list is given in the schema above; return those names, e.g.
              SELECT 'students' AS table_name UNION ALL SELECT 'teachers' ...
              (one line per table listed above).
            - For "rows in each table", UNION a COUNT(*) per table (not information_schema).
            - This message has already been classified as a DATA request, so always
              produce SQL — do not refuse or return anything but SQL.
            - For a short follow-up (e.g. "as a chart", "only males", "sorted by X",
              "top 5", "in Bangla"), adjust the PREVIOUS query in the conversation
              above rather than treating those words as data values.
            - Use only tables/columns from the schema above; never write to the DB.
            - {dialect.PromptSyntaxHint}
            - Return ALL matching rows. Do NOT add LIMIT/TOP unless the user
              explicitly asks to limit the count (e.g. "top 5", "first 10").
              For "all customers" / "list students", return every row (no LIMIT).
            - Return ONLY the raw SQL — no markdown, no comments.

            Question: {question}

            SQL:
            """;
    }

    private static string RenderHistory(IReadOnlyList<ConversationTurn>? history)
    {
        if (history is null || history.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Recent conversation (for context on follow-ups):");
        foreach (var turn in history)
        {
            sb.AppendLine($"- User asked: {turn.Question}");
            if (!string.IsNullOrWhiteSpace(turn.Sql))
                sb.AppendLine($"  SQL used: {turn.Sql}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Rebuild the SQL prompt after a failed execution, telling the model
    /// exactly what went wrong so it can self-correct (e.g. a wrong column).
    /// </summary>
    public string BuildRetryPrompt(
        string question, DatabaseSchema schema, ISqlDialect dialect,
        string failedSql, string dbError, IReadOnlyList<ConversationTurn>? history = null)
    {
        var basePrompt = BuildSqlPrompt(question, schema, dialect, history);
        return $"""
            {basePrompt}

            Your previous attempt FAILED. Do not repeat the same mistake.
            Previous SQL:
            {failedSql}
            Database error:
            {dbError}

            Re-read the schema above and use ONLY column/table names that appear
            there. Return the corrected SQL only.
            """;
    }

    /// <summary>Shared language rule so every conversational reply matches the user's language.</summary>
    private static string LanguageRule(string question) => $"""
        Reply in the same language the user used for "{question}" (English -> English,
        Bangla -> Bangla, Banglish (Bangla in Latin letters) -> Banglish). If they
        earlier gave a standing instruction to use a specific language, keep using it.
        """;

    /// <summary>
    /// Answer a general SQL/DB concept question as a schema-aware SQL tutor.
    /// Explains the concept and, where it helps, illustrates it with an example
    /// grounded in the user's ACTUAL tables — but never runs a query.
    /// </summary>
    public string BuildSqlHelpPrompt(
        string question, DatabaseSchema? schema = null,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var historyText = RenderHistory(history);
        var schemaText = schema is null ? "" : $"""

            The connected database has these tables/columns you can use in examples:
            {RenderSchema(schema)}
            """;
        return $"""
            You are a friendly, expert SQL tutor inside an AI SQL agent. The user
            asked a general SQL/database concept question: "{question}"
            {historyText}{schemaText}
            Explain the concept clearly and briefly (3-6 sentences). Where it helps,
            include ONE small illustrative SQL example — and if a schema is given
            above, base that example on the ACTUAL tables/columns shown so it feels
            concrete. This is teaching, so a short example snippet in your reply is
            welcome, but you are NOT answering a data request: do not claim to have
            run anything or to show real results. End by inviting them to ask about
            their own data if relevant.
            {LanguageRule(question)}
            """;
    }

    /// <summary>
    /// Answer a meta/help request: what the agent can do, how to use it, or for
    /// prompt ideas — grounded in the actual tables so suggestions are usable.
    /// </summary>
    public string BuildMetaHelpPrompt(
        string question, DatabaseSchema? schema = null,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var historyText = RenderHistory(history);
        var schemaText = schema is null ? "" : $"""

            The connected database has these tables/columns:
            {RenderSchema(schema)}
            """;
        return $"""
            You are the assistant of an AI SQL agent that answers questions about a
            database in plain language. The user asked a help/meta question: "{question}"
            {historyText}{schemaText}
            Reply helpfully and briefly (2-4 sentences). Explain that they can ask
            questions about their data in plain language and you'll fetch the answer.
            If a schema is given above, suggest 2-3 concrete example questions
            grounded in the ACTUAL tables/columns (e.g. counts, top-N, per-group,
            trends). Do NOT write SQL. Do NOT invent tables that aren't listed.
            {LanguageRule(question)}
            """;
    }

    /// <summary>
    /// Acknowledge a behavioural instruction (most often a language preference like
    /// "from now on answer in English"). The instruction persists across turns
    /// because the client replays recent history, so here we just confirm it and
    /// invite the next data question. No query is run.
    /// </summary>
    public string BuildInstructionReplyPrompt(
        string question, DatabaseSchema? schema = null,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var historyText = RenderHistory(history);
        return $"""
            You are the assistant of an AI SQL agent that answers questions about a
            database. The user gave an INSTRUCTION about how to behave (not a data
            request): "{question}"
            {historyText}
            Briefly confirm you'll follow it from now on (1-2 sentences) and invite
            them to ask about their data. If it is a language instruction, ACK in
            that requested language and use it going forward. Do NOT write SQL, do
            NOT list example questions.
            {LanguageRule(question)}
            """;
    }

    /// <summary>
    /// Politely handle an off-topic / small-talk message: acknowledge briefly and
    /// steer back to the database. Keeps the agent in scope (not a general chatbot).
    /// </summary>
    public string BuildRedirectPrompt(
        string question, DatabaseSchema? schema = null,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var historyText = RenderHistory(history);
        var schemaText = schema is null ? "" : $"""

            The connected database has these tables: {string.Join(", ", schema.Tables.Select(t => t.Name))}
            """;
        return $"""
            You are the assistant of an AI SQL agent that answers questions about a
            database. The user said something that is small talk or off-topic (not
            about the database or SQL): "{question}"
            {historyText}{schemaText}
            Respond warmly in ONE or TWO short sentences, then gently steer them back:
            make clear you help with their database and SQL, and (if a schema is
            given) hint at one thing they could ask about it. Do NOT answer the
            off-topic question at length or act as a general-purpose assistant.
            Do NOT write SQL.
            {LanguageRule(question)}
            """;
    }

    public string BuildExplanationPrompt(
        string question, string sql, QueryResult result,
        IReadOnlyList<ConversationTurn>? history = null, string? requestedLanguage = null)
    {
        var preview = RenderResultPreview(result);
        var historyText = RenderHistory(history);
        return $"""
            The user asked: "{question}"
            {historyText}
            This SQL was run:
            {sql}

            It returned {result.RowCount} row(s). Data (preview):
            {preview}

            The full data is ALREADY shown to the user in a table, so do NOT
            repeat the rows one by one. Write only a SHORT summary (1-3 sentences):
            the overall count and any notable pattern or highlight (e.g. totals,
            groupings, min/max). Be concise. Do not mention SQL. Do not end with a
            question. If the result is empty, say no matching records were found.

            {LanguageRuleWithOverride(question, requestedLanguage)}
            """;
    }

    /// <summary>
    /// The reply-language rule. If the current message explicitly requested a
    /// language (parsed out during analysis), that wins; otherwise fall back to a
    /// standing instruction, then to matching this question's own language.
    /// </summary>
    private static string LanguageRuleWithOverride(string question, string? requestedLanguage)
    {
        if (!string.IsNullOrWhiteSpace(requestedLanguage))
            return $"""
                Language rule: the user asked for THIS answer to be in
                {requestedLanguage}. Reply in {requestedLanguage}, regardless of the
                language the question itself was written in.
                """;
        return $"""
            Language rule (in priority order):
            1. If the user earlier gave a standing instruction to reply in a
               specific language (e.g. "reply in Bangla from now on", "banglay
               bolo", "answer in Banglish"), keep using THAT language.
            2. Otherwise, match the language/script of THIS question: "{question}"
               — English question -> English answer, Bangla -> Bangla, Banglish
               (Bangla in Latin letters) -> Banglish. Do not just inherit the
               language of earlier messages.
            """;
    }

    private static string RenderResultPreview(QueryResult result, int maxPreviewRows = 20)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(" | ", result.Columns));
        var take = Math.Min(result.Rows.Count, maxPreviewRows);
        for (var r = 0; r < take; r++)
            sb.AppendLine(string.Join(" | ", result.Rows[r].Select(v => v?.ToString() ?? "NULL")));
        if (result.Rows.Count > take)
            sb.AppendLine($"... ({result.Rows.Count - take} more rows)");
        return sb.ToString();
    }
}
