namespace SqlAgent.Domain.Models;

/// <summary>
/// Supported relational database dialects. The runtime connection string
/// determines which dialect the agent talks to. Non-relational stores
/// (e.g. MongoDB) are intentionally out of scope for v1.
/// </summary>
public enum DbDialect
{
    PostgreSql = 0,
    MySql = 1,
    SqlServer = 2
}
