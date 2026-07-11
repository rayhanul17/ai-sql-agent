namespace SqlAgent.Application.Options;

/// <summary>
/// Groq (optional cloud provider). OpenAI-compatible API. The API key should
/// live in a gitignored appsettings.Development.json / env var, not in the
/// committed appsettings.json.
/// </summary>
public sealed class GroqOptions
{
    public const string SectionName = "Groq";

    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";

    /// <summary>API key. Empty in the committed config; set locally (gitignored).</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string DefaultModel { get; set; } = "qwen/qwen3-32b";

    /// <summary>Cloud models offered in the UI when Groq is selected.</summary>
    public List<ModelOption> Models { get; set; } = [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
