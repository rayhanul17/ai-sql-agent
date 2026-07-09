using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Manages the Ollama model catalog: which models are pulled/available,
/// which is currently loaded in RAM, and warming a model up (so the UI can
/// show a loader during a cold switch and log the actual load time).
/// </summary>
public interface IModelManager
{
    /// <summary>Configured + pulled models, flagged with availability/loaded state.</summary>
    Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default);

    /// <summary>Force a model into memory with a tiny prompt; returns the load duration.</summary>
    Task<ModelWarmupResult> WarmUpAsync(string model, CancellationToken ct = default);
}
