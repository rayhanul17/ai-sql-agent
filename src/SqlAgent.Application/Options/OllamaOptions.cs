namespace SqlAgent.Application.Options;

/// <summary>A single configured model tier offered in the UI.</summary>
public sealed class ModelOption
{
    public required string Id { get; set; }          // "qwen2.5-coder:3b"
    public required string DisplayName { get; set; } // "Qwen 2.5 Coder 3B (Minimum)"
    public string? Tier { get; set; }                // "Minimum" / "Almost Best"
}

/// <summary>Bound from the "Ollama" section of appsettings.</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>The default model used when the request does not override it.</summary>
    public string DefaultModel { get; set; } = "qwen2.5-coder:3b";

    /// <summary>Model tiers shown in the UI dropdown (2 for now: 3b + 14b).</summary>
    public List<ModelOption> Models { get; set; } = [];
}
