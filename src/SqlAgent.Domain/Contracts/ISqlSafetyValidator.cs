using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// The SQL safety layer. Never trusts the LLM output: enforces a single
/// read-only SELECT, blocks dangerous keywords and multi-statement payloads,
/// and injects a row limit. This is the app-level guard that complements
/// the read-only DB user / READ ONLY transaction at the execution layer.
/// </summary>
public interface ISqlSafetyValidator
{
    SqlValidationResult Validate(string rawSql, ISqlDialect dialect);
}
