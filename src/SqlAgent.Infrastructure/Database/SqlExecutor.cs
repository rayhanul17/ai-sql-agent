using System.Data;
using System.Data.Common;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Database;

/// <summary>
/// Execution-layer safety net. Runs the already-validated SELECT inside a
/// READ ONLY transaction with a statement timeout. Even if a runtime
/// connection string uses a privileged DB user, the read-only transaction
/// makes the DB itself reject any write the query might attempt.
/// </summary>
public sealed class SqlExecutor : ISqlExecutor
{
    private readonly DbConnectionFactory _connections;

    public SqlExecutor(DbConnectionFactory connections) => _connections = connections;

    public async Task<bool> TestConnectionAsync(
        string connectionString, DbDialect dialect, CancellationToken ct = default)
    {
        try
        {
            await using var conn = _connections.Create(connectionString, dialect);
            await conn.OpenAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<QueryResult> ExecuteReadOnlyAsync(
        string connectionString, DbDialect dialect, string safeSql,
        int timeoutSeconds, CancellationToken ct = default)
    {
        await using var conn = _connections.Create(connectionString, dialect);
        await conn.OpenAsync(ct);

        // Enforce read-only at the DB level, per dialect.
        await BeginReadOnlyAsync(conn, dialect, ct);

        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = safeSql;
        cmd.CommandTimeout = timeoutSeconds;

        var result = new QueryResult();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            for (var i = 0; i < reader.FieldCount; i++)
                result.Columns.Add(reader.GetName(i));

            while (await reader.ReadAsync(ct))
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                result.Rows.Add(row);
            }
        }

        await tx.RollbackAsync(ct); // read-only: never commit
        return result;
    }

    // Postgres & MySQL support SET TRANSACTION READ ONLY as a session/txn guard.
    // SQL Server has no equivalent statement; the validator + read-only login
    // are relied on there (documented limitation).
    private static async Task BeginReadOnlyAsync(DbConnection conn, DbDialect dialect, CancellationToken ct)
    {
        string? guard = dialect switch
        {
            DbDialect.PostgreSql => "SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY;",
            DbDialect.MySql => "SET SESSION TRANSACTION READ ONLY;",
            _ => null
        };
        if (guard is null) return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = guard;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
