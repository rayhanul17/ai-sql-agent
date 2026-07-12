using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Dialects;

/// <summary>
/// SQL Server (T-SQL). Differs from Postgres/MySQL in syntax (e.g. TOP instead
/// of LIMIT) — the LLM is told this via <see cref="PromptSyntaxHint"/>.
/// </summary>
public sealed class SqlServerDialect : ISqlDialect
{
    public DbDialect Dialect => DbDialect.SqlServer;
    public string DisplayName => "Microsoft SQL Server (T-SQL)";

    public string PromptSyntaxHint =>
        "Use T-SQL (Microsoft SQL Server) syntax. If the user asks for a limited " +
        "number of rows, use SELECT TOP n (there is NO LIMIT keyword). " +
        "Quote a table/column name with [square brackets] ([Order]) when it is a " +
        "reserved word or contains special characters. Do NOT use backticks. " +
        "Current time is GETDATE().";

    public string SchemaIntrospectionSql => """
        SELECT
            c.TABLE_NAME AS table_name,
            c.COLUMN_NAME AS column_name,
            c.DATA_TYPE AS data_type,
            c.IS_NULLABLE AS is_nullable,
            CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 'YES' ELSE 'NO' END AS is_pk,
            fk.ref AS fk_reference
        FROM INFORMATION_SCHEMA.COLUMNS c
        LEFT JOIN (
            SELECT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
              ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
             AND tc.CONSTRAINT_SCHEMA = kcu.CONSTRAINT_SCHEMA
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
        ) pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA
            AND pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
        LEFT JOIN (
            SELECT DISTINCT kcu.TABLE_SCHEMA, kcu.TABLE_NAME, kcu.COLUMN_NAME,
                   OBJECT_NAME(fkc.referenced_object_id) + '.' +
                   COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS ref
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
            JOIN sys.foreign_key_columns fkc
              ON fkc.parent_object_id = OBJECT_ID(kcu.TABLE_SCHEMA + '.' + kcu.TABLE_NAME)
             AND COL_NAME(fkc.parent_object_id, fkc.parent_column_id) = kcu.COLUMN_NAME
        ) fk ON fk.TABLE_SCHEMA = c.TABLE_SCHEMA
            AND fk.TABLE_NAME = c.TABLE_NAME AND fk.COLUMN_NAME = c.COLUMN_NAME
        WHERE c.TABLE_SCHEMA = 'dbo'
        ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;
        """;
}
