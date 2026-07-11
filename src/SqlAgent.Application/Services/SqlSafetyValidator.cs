using System.Text.RegularExpressions;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Application.Services;

/// <summary>
/// The SQL safety layer (defense in depth). The LLM is NEVER trusted:
///   1) strip markdown/formatting the model tends to add;
///   2) require a single statement (reject ';'-chained payloads);
///   3) require it to start with SELECT (or WITH ... SELECT);
///   4) reject any dangerous keyword anywhere (write/DDL/exec);
///   5) inject a dialect-aware row limit if missing.
/// The execution layer adds a READ ONLY transaction + read-only DB user
/// as the final backstop.
/// </summary>
public sealed partial class SqlSafetyValidator : ISqlSafetyValidator
{
    // Whole-word, case-insensitive. Covers write, DDL, privilege and exec verbs.
    private static readonly string[] BlockedKeywords =
    [
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "TRUNCATE", "CREATE",
        "GRANT", "REVOKE", "MERGE", "REPLACE", "EXEC", "EXECUTE", "CALL",
        "COPY", "INTO", "ATTACH", "PRAGMA", "VACUUM", "COMMIT", "ROLLBACK"
    ];

    public SqlValidationResult Validate(string rawSql, ISqlDialect dialect, int maxRows)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
            return SqlValidationResult.Invalid("Empty query.");

        var sql = Clean(rawSql);

        if (sql.Length == 0)
            return SqlValidationResult.Invalid("No SQL found after cleaning model output.");

        // (2) Keep only the first statement. Models sometimes append a trailing
        // ';' plus commentary/echo after the SQL; taking everything up to the
        // first ';' both tolerates that and stays safe — any second statement
        // (e.g. "...; DROP ...") is discarded and never executed. A single
        // UNION [ALL] SELECT is ONE statement (no ';' inside), so it passes.
        var firstSemicolon = sql.IndexOf(';');
        if (firstSemicolon >= 0)
            sql = sql[..firstSemicolon];
        sql = sql.Trim();

        if (sql.Length == 0)
            return SqlValidationResult.Invalid("No SQL statement found.");

        // (3) Must be a read-only SELECT (optionally a CTE: WITH ... SELECT).
        var head = sql.TrimStart('(', ' ', '\r', '\n', '\t');
        var startsSelect = head.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
        var startsWith = head.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
        if (!startsSelect && !startsWith)
            return SqlValidationResult.Invalid("Only read-only SELECT queries are allowed.");

        // (4) No dangerous keyword anywhere (whole-word match).
        foreach (var kw in BlockedKeywords)
        {
            if (Regex.IsMatch(sql, $@"\b{kw}\b", RegexOptions.IgnoreCase))
                return SqlValidationResult.Invalid($"Blocked keyword detected: {kw}.");
        }

        // (5) Force a row limit if the dialect says one is missing.
        var safe = dialect.HasRowLimit(sql) ? sql : dialect.ApplyRowLimit(sql, maxRows);

        return SqlValidationResult.Valid(safe);
    }

    /// <summary>Strip reasoning blocks, code fences, "sql:" prefixes, and whitespace.</summary>
    private static string Clean(string raw)
    {
        var s = raw.Trim();

        // Reasoning models (e.g. Qwen3, DeepSeek-R1) emit a <think>...</think>
        // block before the answer. Remove it so only the SQL remains.
        s = ThinkRegex().Replace(s, string.Empty).Trim();

        // Remove markdown code fences ```sql ... ```
        s = FenceRegex().Replace(s, m => m.Groups["body"].Value);

        // Remove a leading "sql" label if the model prefixed one.
        s = LeadingLabelRegex().Replace(s, string.Empty);

        return s.Trim();
    }

    // Matches a whole <think>...</think> block, and also a dangling "<think>...">
    // with no close tag (some models get cut off before the closing tag).
    [GeneratedRegex(@"<think>.*?(</think>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkRegex();

    [GeneratedRegex(@"```(?:sql)?\s*(?<body>.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"^\s*sql\s*[:>]?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingLabelRegex();
}
