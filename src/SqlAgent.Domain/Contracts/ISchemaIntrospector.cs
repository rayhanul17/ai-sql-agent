using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Reads the live schema (tables, columns, keys) of a connected database
/// so the prompt can be grounded in real structure (prevents the LLM from
/// inventing columns/tables).
/// </summary>
public interface ISchemaIntrospector
{
    Task<DatabaseSchema> IntrospectAsync(
        string connectionString,
        DbDialect dialect,
        CancellationToken ct = default);
}
