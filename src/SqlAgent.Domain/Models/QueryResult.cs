namespace SqlAgent.Domain.Models;

/// <summary>
/// The tabular result of executing a validated read-only query.
/// Columns + rows are kept generic (object?) because the target schema
/// is not known at compile time (runtime connection strings).
/// </summary>
public sealed class QueryResult
{
    public List<string> Columns { get; init; } = [];
    public List<object?[]> Rows { get; init; } = [];

    public int RowCount => Rows.Count;

    /// <summary>True when the result is a single numeric/aggregate value.</summary>
    public bool IsScalar => Columns.Count == 1 && Rows.Count == 1;
}
