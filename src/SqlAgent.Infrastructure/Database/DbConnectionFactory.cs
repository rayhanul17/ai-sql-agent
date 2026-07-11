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
        DbDialect.SqlServer => new SqlConnection(SqlServerReadOnly(connectionString)),
        _ => throw new NotSupportedException($"Dialect {dialect} is not supported.")
    };

    // SQL Server has no "SET TRANSACTION READ ONLY". Signal read-only intent on
    // the connection instead (ApplicationIntent=ReadOnly) so a read-only replica
    // is used where one exists; combined with the SELECT-only validator and the
    // rolled-back transaction this is the SQL Server read-only story.
    private static string SqlServerReadOnly(string connectionString)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(connectionString)
            {
                ApplicationIntent = ApplicationIntent.ReadOnly
            };
            return b.ConnectionString;
        }
        catch
        {
            return connectionString; // malformed string: let SqlConnection surface the error
        }
    }
}
