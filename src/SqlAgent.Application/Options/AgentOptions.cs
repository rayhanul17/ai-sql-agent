using SqlAgent.Domain.Models;

namespace SqlAgent.Application.Options;

/// <summary>Bound from the "Agent" section of appsettings.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Max rows any generated query may return (row-limit safety).</summary>
    public int MaxRows { get; set; } = 100;

    /// <summary>Statement timeout for executed queries.</summary>
    public int QueryTimeoutSeconds { get; set; } = 30;

    /// <summary>Default demo database connection string (used when the user gives none).</summary>
    public string DefaultConnectionString { get; set; } = string.Empty;

    /// <summary>Dialect of the default demo database.</summary>
    public DbDialect DefaultDialect { get; set; } = DbDialect.PostgreSql;
}
