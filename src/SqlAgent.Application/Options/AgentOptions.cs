using SqlAgent.Domain.Models;

namespace SqlAgent.Application.Options;

/// <summary>Bound from the "Agent" section of appsettings.</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    // No row cap and no schema-table cap: full results and the full schema are
    // used, so demos always show complete data. (Very large cloud prompts can
    // hit a provider's token/rate limit — a documented provider-side limit,
    // not something we truncate around.)

    /// <summary>Statement timeout for executed queries.</summary>
    public int QueryTimeoutSeconds { get; set; } = 30;

    /// <summary>Default demo database connection string (used when the user gives none).</summary>
    public string DefaultConnectionString { get; set; } = string.Empty;

    /// <summary>Dialect of the default demo database.</summary>
    public DbDialect DefaultDialect { get; set; } = DbDialect.PostgreSql;

    /// <summary>Default LLM provider (Ollama is local/primary).</summary>
    public LlmProvider DefaultProvider { get; set; } = LlmProvider.Ollama;
}
