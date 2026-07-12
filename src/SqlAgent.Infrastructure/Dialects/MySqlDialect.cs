using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Dialects;

public sealed class MySqlDialect : ISqlDialect
{
    public DbDialect Dialect => DbDialect.MySql;
    public string DisplayName => "MySQL";

    public string PromptSyntaxHint =>
        "Use MySQL syntax. Quote a table/column name with backticks (`order`) when it " +
        "is a reserved word or contains special characters. Do NOT use double quotes " +
        "or [brackets] for identifiers. " +
        "If the user asks for a limited number of rows, use LIMIT n. Current time is NOW().";

    // MySQL uses DATABASE() to scope to the current schema.
    public string SchemaIntrospectionSql => """
        SELECT
            c.TABLE_NAME AS table_name,
            c.COLUMN_NAME AS column_name,
            c.DATA_TYPE AS data_type,
            c.IS_NULLABLE AS is_nullable,
            CASE WHEN c.COLUMN_KEY = 'PRI' THEN 'YES' ELSE 'NO' END AS is_pk,
            CASE WHEN kcu.REFERENCED_TABLE_NAME IS NOT NULL
                 THEN CONCAT(kcu.REFERENCED_TABLE_NAME, '.', kcu.REFERENCED_COLUMN_NAME)
                 ELSE NULL END AS fk_reference
        FROM INFORMATION_SCHEMA.COLUMNS c
        LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
          ON kcu.TABLE_SCHEMA = c.TABLE_SCHEMA
         AND kcu.TABLE_NAME = c.TABLE_NAME
         AND kcu.COLUMN_NAME = c.COLUMN_NAME
         AND kcu.REFERENCED_TABLE_NAME IS NOT NULL
        WHERE c.TABLE_SCHEMA = DATABASE()
        ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;
        """;
}
