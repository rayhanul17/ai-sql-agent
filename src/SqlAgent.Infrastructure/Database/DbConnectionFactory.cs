using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Database;

/// <summary>
/// Creates the right ADO.NET connection for a runtime connection string,
/// based on the chosen dialect. Each dialect ships its own free provider:
/// Npgsql / MySqlConnector / Microsoft.Data.SqlClient (all MIT-licensed).
/// </summary>
public sealed class DbConnectionFactory
{
    public DbConnection Create(string connectionString, DbDialect dialect) => dialect switch
    {
        DbDialect.PostgreSql => new NpgsqlConnection(connectionString),
        DbDialect.MySql => new MySqlConnection(connectionString),
        DbDialect.SqlServer => new SqlConnection(connectionString),
        _ => throw new NotSupportedException($"Dialect {dialect} is not supported.")
    };
}
