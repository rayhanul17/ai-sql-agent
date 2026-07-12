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
            - Reply with the single token NO_QUERY (nothing else) whenever the
              message is NOT a request for specific rows/values FROM the tables.
              This includes greetings/thanks AND any meta/help/suggestion request,
              e.g. "what can you do", "how do I use this", "give me ideas", and
              crucially "suggest me a prompt/question", "suggest a query", "what
              should I ask", "help me get insight from the data". Asking you to
              SUGGEST or RECOMMEND a prompt/question is NOT a data query — return
              NO_QUERY. Only produce SQL when the user directly asks for data
              (counts, lists, specific records), not for ideas about what to ask.
            - Use only tables/columns from the schema above; never write to the DB.
            - {dialect.PromptSyntaxHint}
            - For a follow-up like "in Bangla" / "as a chart" / "only males", adjust
              the previous query rather than treating those words as data values.
            - Return ALL matching rows. Do NOT add LIMIT/TOP unless the user
              explicitly asks to limit the count (e.g. "top 5", "first 10").
              For "all customers" / "list students", return every row (no LIMIT).
            - Return ONLY the raw SQL (or NO_QUERY) — no markdown, no comments.

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

    /// <summary>
    /// A short, friendly reply for non-data messages (greetings/small talk),
    /// steering the user back toward asking about their database.
    /// </summary>
    public string BuildChatReplyPrompt(
        string question, DatabaseSchema? schema = null,
        IReadOnlyList<ConversationTurn>? history = null)
    {
        var historyText = RenderHistory(history);
        var schemaText = schema is null ? "" : $"""

            The connected database has these tables/columns:
            {RenderSchema(schema)}
            """;
        return $"""
            You are the assistant of an AI SQL agent that answers questions about
            a database. The user said: "{question}"
            {historyText}{schemaText}
            This is NOT a request to retrieve data — it's small talk or a help/meta
            question (e.g. a greeting, "what can you do", or "suggest a prompt").
            Reply helpfully and briefly (2-4 sentences). If they asked for prompt
            ideas or what they can ask, suggest 2-3 concrete example questions
            grounded in the ACTUAL tables/columns above (e.g. counts, top-N,
            per-group, trends). Do NOT write SQL. Reply in the same language the
            user used (English question -> English; if they earlier asked for a
            language, keep using it).
            """;
    }

    public string BuildExplanationPrompt(
        string question, string sql, QueryResult result,
        IReadOnlyList<ConversationTurn>? history = null)
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
