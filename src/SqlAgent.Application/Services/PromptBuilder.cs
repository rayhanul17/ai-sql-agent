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

    public string BuildSqlPrompt(string question, DatabaseSchema schema, ISqlDialect dialect, int maxRows)
    {
        var schemaText = RenderSchema(schema);
        return $"""
            You are a senior data analyst that writes {dialect.DisplayName} SQL.

            Database schema (only these tables/columns exist):
            {schemaText}

            Rules — follow ALL strictly:
            - Generate exactly ONE read-only SELECT statement.
            - NEVER use INSERT, UPDATE, DELETE, DROP, ALTER, TRUNCATE, CREATE, GRANT or any write/DDL.
            - NEVER invent tables or columns that are not in the schema above.
            - {dialect.PromptSyntaxHint}
            - Always limit the result to at most {maxRows} rows.
            - Return ONLY the raw SQL. No explanation, no markdown fences, no comments.

            Question: {question}

            SQL:
            """;
    }

    public string BuildExplanationPrompt(string question, string sql, QueryResult result)
    {
        var preview = RenderResultPreview(result);
        return $"""
            The user asked: "{question}"

            This SQL was run:
            {sql}

            It returned {result.RowCount} row(s). Data (preview):
            {preview}

            Write a concise, friendly answer to the user's question based ONLY on this data.
            Do not mention SQL. If the result is empty, say no matching records were found.
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
