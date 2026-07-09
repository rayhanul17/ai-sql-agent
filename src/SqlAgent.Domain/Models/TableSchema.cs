namespace SqlAgent.Domain.Models;

/// <summary>A single column within a table.</summary>
public sealed class ColumnSchema
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool IsNullable { get; init; }
    public bool IsPrimaryKey { get; init; }

    /// <summary>Set when this column is a foreign key referencing another table.</summary>
    public string? ForeignKeyReference { get; init; }
}

/// <summary>A single table (or view) with its columns.</summary>
public sealed class TableSchema
{
    public required string Schema { get; init; }
    public required string Name { get; init; }
    public List<ColumnSchema> Columns { get; init; } = [];
}

/// <summary>
/// The full introspected schema of a connected database. Cached per
/// connection and rendered into a compact text form for the LLM prompt.
/// </summary>
public sealed class DatabaseSchema
{
    public required DbDialect Dialect { get; init; }
    public List<TableSchema> Tables { get; init; } = [];
}
