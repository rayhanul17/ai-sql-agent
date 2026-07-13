using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Abstraction over the LLM (implemented with Semantic Kernel + Ollama).
/// The rest of the app never depends on Ollama directly, so a cloud LLM
/// (OpenAI/Claude) could be swapped in by configuration only.
/// </summary>
public interface IAiProvider
{
    /// <summary>Which provider this implementation serves.</summary>
    LlmProvider Provider { get; }

    /// <summary>
    /// Generate a single SQL statement (or intent label) from a grounded prompt
    /// (non-streamed). An optional system message carries the stable agent rules /
    /// safety guard as a separate role, keeping them apart from the user content.
    /// </summary>
    Task<string> GenerateSqlAsync(string prompt, string model, string? systemMessage = null, CancellationToken ct = default);

    /// <summary>
    /// Stream a natural-language explanation of the query result, token by token.
    /// </summary>
    IAsyncEnumerable<string> StreamExplanationAsync(
        string prompt, string model, string? systemMessage = null, CancellationToken ct = default);
}

/// <summary>Resolves the right <see cref="IAiProvider"/> for a given <see cref="LlmProvider"/>.</summary>
public interface IAiProviderResolver
{
    IAiProvider Get(LlmProvider provider);
}
