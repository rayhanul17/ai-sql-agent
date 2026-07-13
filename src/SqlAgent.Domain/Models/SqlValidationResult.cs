namespace SqlAgent.Domain.Models;

/// <summary>
/// Outcome of the SQL safety layer. A query only executes when
/// <see cref="IsValid"/> is true; <see cref="SafeSql"/> may differ from the LLM's
/// raw SQL because it is cleaned (markdown/reasoning stripped) and truncated to
/// the first statement.
/// </summary>
public sealed class SqlValidationResult
{
    public bool IsValid { get; private init; }
    public string? SafeSql { get; private init; }
    public string? Reason { get; private init; }

    public static SqlValidationResult Valid(string safeSql) =>
        new() { IsValid = true, SafeSql = safeSql };

    public static SqlValidationResult Invalid(string reason) =>
        new() { IsValid = false, Reason = reason };
}
