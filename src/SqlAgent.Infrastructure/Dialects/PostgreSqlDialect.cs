using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Dialects;

public sealed class PostgreSqlDialect : LimitDialectBase
{
    public override DbDialect Dialect => DbDialect.PostgreSql;
    public override string DisplayName => "PostgreSQL";

    public override string PromptSyntaxHint =>
        "Use PostgreSQL syntax. Quote identifiers with double quotes only if needed. " +
        "Use LIMIT n to cap rows. Current time is NOW().";

    // Returns: table_name, column_name, data_type, is_nullable, is_pk, fk_reference
    public override string SchemaIntrospectionSql => """
        SELECT
            c.table_name,
            c.column_name,
            c.data_type,
            c.is_nullable,
            CASE WHEN pk.column_name IS NOT NULL THEN 'YES' ELSE 'NO' END AS is_pk,
            fk.ref AS fk_reference
        FROM information_schema.columns c
        LEFT JOIN (
            SELECT kcu.table_name, kcu.column_name
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY'
        ) pk ON pk.table_name = c.table_name AND pk.column_name = c.column_name
        LEFT JOIN (
            SELECT kcu.table_name, kcu.column_name,
                   ccu.table_name || '.' || ccu.column_name AS ref
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
        ) fk ON fk.table_name = c.table_name AND fk.column_name = c.column_name
        WHERE c.table_schema = 'public'
        ORDER BY c.table_name, c.ordinal_position;
        """;
}
