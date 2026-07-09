using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Dialects;

public sealed class SqlDialectFactory : ISqlDialectFactory
{
    private readonly Dictionary<DbDialect, ISqlDialect> _dialects;

    public SqlDialectFactory(IEnumerable<ISqlDialect> dialects) =>
        _dialects = dialects.ToDictionary(d => d.Dialect);

    public ISqlDialect Get(DbDialect dialect) =>
        _dialects.TryGetValue(dialect, out var d)
            ? d
            : throw new NotSupportedException($"Dialect {dialect} is not supported.");
}
