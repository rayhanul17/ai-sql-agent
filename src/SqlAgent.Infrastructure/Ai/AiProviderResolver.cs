using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Ai;

/// <summary>Resolves the registered <see cref="IAiProvider"/> for a provider enum.</summary>
public sealed class AiProviderResolver : IAiProviderResolver
{
    private readonly Dictionary<LlmProvider, IAiProvider> _providers;

    public AiProviderResolver(IEnumerable<IAiProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Provider);

    public IAiProvider Get(LlmProvider provider) =>
        _providers.TryGetValue(provider, out var p)
            ? p
            : throw new NotSupportedException($"LLM provider {provider} is not configured.");
}
