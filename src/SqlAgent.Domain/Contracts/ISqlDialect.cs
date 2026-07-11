using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Encapsulates the small differences between relational dialects
/// (PostgreSQL, MySQL, SQL Server): how schema is read and how the dialect's
/// syntax is described to the LLM. One implementation per <see cref="DbDialect"/>.
/// </summary>
public interface ISqlDialect
{
    DbDialect Dialect { get; }

    /// <summary>Human/prompt-friendly name, e.g. "PostgreSQL", "Microsoft SQL Server (T-SQL)".</summary>
    string DisplayName { get; }

    /// <summary>SQL that returns tables/columns/keys for schema introspection.</summary>
    string SchemaIntrospectionSql { get; }

    /// <summary>Prompt guidance describing this dialect's syntax rules for the LLM.</summary>
    string PromptSyntaxHint { get; }
}
