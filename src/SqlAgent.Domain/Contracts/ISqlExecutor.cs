using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Executes a validated SELECT against the target database inside a
/// READ ONLY transaction with a statement timeout. This is the execution-layer
/// safety net: even if the app-level guard were bypassed, the DB refuses writes.
/// </summary>
public interface ISqlExecutor
{
    Task<QueryResult> ExecuteReadOnlyAsync(
        string connectionString,
        DbDialect dialect,
        string safeSql,
        int timeoutSeconds,
        int maxRows = 0,
        CancellationToken ct = default);

    /// <summary>Verifies a runtime connection string can connect (used before first query).</summary>
    Task<bool> TestConnectionAsync(
        string connectionString,
        DbDialect dialect,
        CancellationToken ct = default);
}
