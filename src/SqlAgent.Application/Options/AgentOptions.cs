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

    /// <summary>
    /// Safety cap on rows returned by a single query. 0 = unlimited (the default,
    /// so demos show full data). When &gt; 0, the data reader stops after this many
    /// rows as a hard backstop against a runaway result set — independent of any
    /// LIMIT the SQL itself may or may not have.
    /// </summary>
    public int MaxRows { get; set; } = 0;

    /// <summary>
    /// Max time to wait for a single LLM call (classify / generate SQL / explain)
    /// before giving up. Stops a hung or throttled provider (e.g. a Groq rate-limit
    /// that never returns) from leaving the request pending forever — the user gets
    /// a clear error instead of an endless spinner.
    /// </summary>
    public int LlmTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// How long a cached schema stays valid before it is re-read from the DB.
    /// A safety net against a schema that changed after it was cached; the query
    /// path also force-refreshes on a DB error (see QueryAgentService). Set to 0
    /// to disable time-based expiry (cache until manual refresh / restart).
    /// </summary>
    public int SchemaCacheTtlMinutes { get; set; } = 30;

    /// <summary>Default demo database connection string (used when the user gives none).</summary>
    public string DefaultConnectionString { get; set; } = string.Empty;

    /// <summary>Dialect of the default demo database.</summary>
    public DbDialect DefaultDialect { get; set; } = DbDialect.PostgreSql;

    /// <summary>Default LLM provider (Ollama is local/primary).</summary>
    public LlmProvider DefaultProvider { get; set; } = LlmProvider.Ollama;
}
