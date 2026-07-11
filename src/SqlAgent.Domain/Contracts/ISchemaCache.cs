using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Caches an introspected <see cref="DatabaseSchema"/> per data source so the
/// full structure is read once (on Save) instead of on every query. Keyed by
/// connection string + dialect. Invalidated on connection change / refresh.
/// </summary>
public interface ISchemaCache
{
    bool TryGet(string connectionString, DbDialect dialect, out DatabaseSchema schema);
    void Set(string connectionString, DbDialect dialect, DatabaseSchema schema);
    void Invalidate(string connectionString, DbDialect dialect);
}
