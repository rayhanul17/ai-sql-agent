using System.Text.RegularExpressions;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Dialects;

/// <summary>
/// Shared behaviour for dialects that use the LIMIT keyword (PostgreSQL, MySQL).
/// SQL Server differs (TOP) and does not inherit this.
/// </summary>
public abstract class LimitDialectBase : ISqlDialect
{
    public abstract DbDialect Dialect { get; }
    public abstract string DisplayName { get; }
    public abstract string SchemaIntrospectionSql { get; }
    public abstract string PromptSyntaxHint { get; }

    private static readonly Regex LimitRegex =
        new(@"\bLIMIT\s+\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool HasRowLimit(string sql) => LimitRegex.IsMatch(sql);

    public string ApplyRowLimit(string sql, int maxRows)
    {
        if (HasRowLimit(sql)) return sql;
        return $"{sql.TrimEnd()}\nLIMIT {maxRows}";
    }
}
