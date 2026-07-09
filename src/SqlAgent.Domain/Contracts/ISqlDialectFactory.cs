using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>Resolves the <see cref="ISqlDialect"/> for a given <see cref="DbDialect"/>.</summary>
public interface ISqlDialectFactory
{
    ISqlDialect Get(DbDialect dialect);
}
