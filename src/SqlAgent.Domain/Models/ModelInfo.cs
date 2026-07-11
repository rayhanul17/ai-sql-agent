namespace SqlAgent.Domain.Models;

/// <summary>
/// A selectable Ollama model (a "tier"). The UI dropdown offers only
/// models that are actually pulled/available.
/// </summary>
public sealed class ModelInfo
{
    public required string Id { get; init; }        // e.g. "qwen2.5-coder:3b"
    public required string DisplayName { get; init; } // e.g. "Qwen 2.5 Coder 3B (Minimum)"
    public LlmProvider Provider { get; init; }      // Ollama or Groq
    public string? Tier { get; init; }              // e.g. "Minimum" / "Almost Best"
    public bool IsAvailable { get; init; }          // Ollama: pulled; Groq: key configured
    public bool IsLoaded { get; init; }             // Ollama: resident in RAM; Groq: always false
}

/// <summary>Result of warming up (loading) a model into memory.</summary>
public sealed class ModelWarmupResult
{
    public required string ModelId { get; init; }
    public bool Success { get; init; }
    public long LoadDurationMs { get; init; }       // from Ollama "load_duration"
    public string? Error { get; init; }
}
