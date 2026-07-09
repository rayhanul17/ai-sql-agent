using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Encapsulates the small differences between relational dialects
/// (PostgreSQL, MySQL, SQL Server): how to limit rows, how schema is read,
/// and how identifiers/read-only transactions are expressed.
/// One implementation per <see cref="DbDialect"/>.
/// </summary>
public interface ISqlDialect
{
    DbDialect Dialect { get; }

    /// <summary>Human/prompt-friendly name, e.g. "PostgreSQL", "Microsoft SQL Server (T-SQL)".</summary>
    string DisplayName { get; }

    /// <summary>SQL that returns tables/columns/keys for schema introspection.</summary>
    string SchemaIntrospectionSql { get; }

    /// <summary>
    /// Ensure the query returns at most <paramref name="maxRows"/> rows.
    /// Postgres/MySQL append LIMIT; SQL Server injects TOP.
    /// Returns the SQL unchanged if a limit is already present.
    /// </summary>
    string ApplyRowLimit(string sql, int maxRows);

    /// <summary>True if the SQL already constrains the row count (LIMIT/TOP/FETCH).</summary>
    bool HasRowLimit(string sql);

    /// <summary>Prompt guidance describing this dialect's syntax rules for the LLM.</summary>
    string PromptSyntaxHint { get; }
}
