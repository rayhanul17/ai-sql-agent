using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    public OllamaModelManager(HttpClient http, IOptions<OllamaOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(CancellationToken ct = default)
    {
        var available = await GetNamesAsync("/api/tags", ct);
        var loaded = await GetNamesAsync("/api/ps", ct);

        return _options.Models.Select(m => new ModelInfo
        {
            Id = m.Id,
            DisplayName = m.DisplayName,
            Tier = m.Tier,
            IsAvailable = available.Contains(m.Id),
            IsLoaded = loaded.Contains(m.Id)
        }).ToList();
    }

    public async Task<ModelWarmupResult> WarmUpAsync(string model, CancellationToken ct = default)
    {
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
            return new ModelWarmupResult { ModelId = model, Success = false, Error = ex.Message };
        }
    }

    private async Task<HashSet<string>> GetNamesAsync(string path, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<ModelListResponse>(path, ct);
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
