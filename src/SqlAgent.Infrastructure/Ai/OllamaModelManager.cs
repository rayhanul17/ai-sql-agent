using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlAgent.Application.Options;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Ai;

/// <summary>
/// Talks to Ollama's management endpoints (not covered by Semantic Kernel):
///   GET  /api/tags     -> which models are pulled/available
///   GET  /api/ps       -> which models are currently resident in RAM
///   POST /api/generate -> a tiny warm-up prompt that forces a cold load,
///                         returning load_duration so the UI can show a loader.
/// </summary>
public sealed class OllamaModelManager : IModelManager
{
    private readonly HttpClient _http;
    private readonly OllamaOptions _options;
    private readonly GroqOptions _groq;
    private readonly ILogger<OllamaModelManager> _log;

    public OllamaModelManager(
        HttpClient http, IOptions<OllamaOptions> options, IOptions<GroqOptions> groq,
        ILogger<OllamaModelManager> log)
    {
        _http = http;
        _options = options.Value;
        _groq = groq.Value;
        _log = log;
        _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
    {
        var available = await GetNamesAsync("/api/tags", ct);
        var loaded = await GetNamesAsync("/api/ps", ct);

        var models = _options.Models.Select(m => new ModelInfo
        {
            Id = m.Id,
            DisplayName = m.DisplayName,
            Provider = LlmProvider.Ollama,
            Tier = m.Tier,
            IsAvailable = available.Contains(m.Id),
            IsLoaded = loaded.Contains(m.Id)
        }).ToList();

        // Groq cloud models: available only when an API key is configured.
        models.AddRange(_groq.Models.Select(m => new ModelInfo
        {
            Id = m.Id,
            DisplayName = m.DisplayName,
            Provider = LlmProvider.Groq,
            Tier = m.Tier,
            IsAvailable = _groq.IsConfigured,
            IsLoaded = false
        }));

        return models;
    }

    public async Task<ModelWarmupResult> WarmUpAsync(string model, CancellationToken ct = default)
    {
        // Cloud (Groq) models need no warm-up; report success immediately.
        if (_groq.Models.Any(m => string.Equals(m.Id, model, StringComparison.OrdinalIgnoreCase)))
            return new ModelWarmupResult { ModelId = model, Success = true, LoadDurationMs = 0 };

        try
        {
            var payload = new { model, prompt = "hi", stream = false };
            using var resp = await _http.PostAsJsonAsync("/api/generate", payload, ct);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken: ct);
            var loadMs = (body?.LoadDuration ?? 0) / 1_000_000; // ns -> ms
            return new ModelWarmupResult { ModelId = model, Success = true, LoadDurationMs = loadMs };
        }
        catch (Exception ex)
        {
            // Log the full detail server-side; return a generic, safe message so
            // driver/connection internals never reach the UI.
            _log.LogWarning(ex, "Model warm-up failed for {Model}", model);
            return new ModelWarmupResult
            {
                ModelId = model,
                Success = false,
                Error = "Could not load or check the selected model. Make sure Ollama is running, " +
                        "the model exists, or the cloud provider is configured."
            };
        }
    }

    private async Task<HashSet<string>> GetNamesAsync(string path, CancellationToken ct)
    {
        try
        {
            // The shared HttpClient has a long timeout for model warm-up; the
            // lightweight list calls must fail fast if Ollama is down.
            using var quick = CancellationTokenSource.CreateLinkedTokenSource(ct);
            quick.CancelAfter(TimeSpan.FromSeconds(3));
            var resp = await _http.GetFromJsonAsync<ModelListResponse>(path, quick.Token);
            return resp?.Models?.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
                   ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Ollama not reachable -> treat everything as unavailable rather than crash.
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class ModelListResponse
    {
        [JsonPropertyName("models")] public List<ModelEntry>? Models { get; set; }
    }

    private sealed class ModelEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    private sealed class GenerateResponse
    {
        [JsonPropertyName("load_duration")] public long LoadDuration { get; set; }
    }
}
