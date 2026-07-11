using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Manages the Ollama model catalog: which models are pulled/available,
/// which is currently loaded in RAM, and warming a model up (so the UI can
/// show a loader during a cold switch and log the actual load time).
/// </summary>
public interface IModelManager
{
    /// <summary>
    /// All configured models across providers, flagged with availability/loaded
    /// state (Ollama: pulled/resident; Groq: available iff an API key is set).
    /// </summary>
    Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Force an Ollama model into memory (returns load duration). For cloud
    /// providers this is a no-op success (no warm-up needed).
    /// </summary>
    Task<ModelWarmupResult> WarmUpAsync(string model, CancellationToken ct = default);
}
