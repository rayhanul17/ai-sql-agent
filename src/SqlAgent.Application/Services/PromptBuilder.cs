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
        string question, DatabaseSchema schema, ISqlDialect dialect, int maxRows,
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
            - For "rows in each table", UNION a COUNT(*) per table (not information_schema).
            - Only if the message is purely a greeting/thanks with no data intent,
              reply with the single token NO_QUERY instead.
            - Use only tables/columns from the schema above; never write to the DB.
            - {dialect.PromptSyntaxHint}
            - For a follow-up like "in Bangla" / "as a chart" / "only males", adjust
              the previous query rather than treating those words as data values.
            - Limit results to at most {maxRows} rows.
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
        string question, DatabaseSchema schema, ISqlDialect dialect, int maxRows,
        string failedSql, string dbError, IReadOnlyList<ConversationTurn>? history = null)
    {
        var basePrompt = BuildSqlPrompt(question, schema, dialect, maxRows, history);
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
    public string BuildChatReplyPrompt(string question, IReadOnlyList<ConversationTurn>? history = null)
    {
        var historyText = RenderHistory(history);
        return $"""
            The user said: "{question}"
            {historyText}
            This is not a database question. Reply briefly and warmly (1-2 sentences),
            and gently invite them to ask something about their data (for example,
            counts, top-N, or filtered lists). Do not write any SQL.
            Reply in the same language the user used.
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

            Reply in the same language the user used (Bangla question -> Bangla answer).
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
