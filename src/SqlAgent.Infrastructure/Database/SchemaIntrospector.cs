using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Database;

/// <summary>
/// Reads tables/columns/keys from the connected database using the dialect's
/// introspection SQL, and groups the flat rows into a <see cref="DatabaseSchema"/>.
/// </summary>
public sealed class SchemaIntrospector : ISchemaIntrospector
{
    private readonly DbConnectionFactory _connections;
    private readonly ISqlDialectFactory _dialects;

    public SchemaIntrospector(DbConnectionFactory connections, ISqlDialectFactory dialects)
    {
        _connections = connections;
        _dialects = dialects;
    }

    public async Task<DatabaseSchema> IntrospectAsync(
        string connectionString, DbDialect dialect, CancellationToken ct = default)
    {
        var d = _dialects.Get(dialect);
        var tables = new Dictionary<string, TableSchema>(StringComparer.OrdinalIgnoreCase);

        await using var conn = _connections.Create(connectionString, dialect);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = d.SchemaIntrospectionSql;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var tableName = reader.GetString(0);
            var column = new ColumnSchema
            {
                Name = reader.GetString(1),
                DataType = reader.GetString(2),
                IsNullable = string.Equals(GetNullableString(reader, 3), "YES", StringComparison.OrdinalIgnoreCase),
                IsPrimaryKey = string.Equals(GetNullableString(reader, 4), "YES", StringComparison.OrdinalIgnoreCase),
                ForeignKeyReference = reader.IsDBNull(5) ? null : reader.GetString(5)
            };

            if (!tables.TryGetValue(tableName, out var table))
            {
                table = new TableSchema { Schema = string.Empty, Name = tableName };
                tables[tableName] = table;
            }
            // A column in multiple/composite foreign keys yields several rows in
            // the MySQL/SQL Server introspection joins; keep only the first so
            // the schema shown to the model has no duplicate columns.
            if (!table.Columns.Any(c => string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase)))
                table.Columns.Add(column);
        }

        return new DatabaseSchema { Dialect = dialect, Tables = tables.Values.ToList() };
    }

    private static string? GetNullableString(System.Data.Common.DbDataReader r, int i) =>
        r.IsDBNull(i) ? null : r.GetValue(i)?.ToString();
}
