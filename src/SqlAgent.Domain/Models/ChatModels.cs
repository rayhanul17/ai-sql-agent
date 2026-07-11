namespace SqlAgent.Domain.Models;

/// <summary>One earlier exchange, sent back by the client for context.</summary>
public sealed class ConversationTurn
{
    public required string Question { get; init; }
    public string? Sql { get; init; }
}

/// <summary>A user's natural-language question plus the data source + model to use.</summary>
public sealed class AskRequest
{
    public required string Question { get; init; }

    /// <summary>Optional runtime connection string. When null, the default demo DB is used.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>Dialect of the provided connection string. Ignored when using the demo DB.</summary>
    public DbDialect? Dialect { get; init; }

    /// <summary>Which LLM provider to use. Null = configured default (Ollama).</summary>
    public LlmProvider? Provider { get; init; }

    /// <summary>Optional model override (must be an available model). Null = configured default.</summary>
    public string? Model { get; init; }

    /// <summary>Recent prior turns (oldest first) so follow-up questions resolve.</summary>
    public List<ConversationTurn> History { get; init; } = [];
}

/// <summary>The generated SQL plus its execution result and an AI explanation.</summary>
public sealed class AskResult
{
    public required string Question { get; init; }
    public string? GeneratedSql { get; init; }
    public QueryResult? Result { get; init; }
    public string? Explanation { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? ModelUsed { get; init; }
}

/// <summary>A streamed chunk sent over SSE while answering.</summary>
public sealed class StreamChunk
{
    public required string Type { get; init; } // "status" | "sql" | "rows" | "token" | "done" | "error"
    public string? Content { get; init; }
    public object? Data { get; init; }
}
