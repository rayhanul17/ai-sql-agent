using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Ai;

/// <summary>
/// Optional cloud provider. Groq exposes an OpenAI-compatible API, so we reuse
/// Semantic Kernel's OpenAI connector, pointed at Groq's base URL. Fast enough
/// that no warm-up/loading step is needed. Model is chosen per request.
/// </summary>
public sealed class GroqAiProvider : IAiProvider
{
    private readonly string _apiKey;
    private readonly Uri _baseUri;

    public GroqAiProvider(string apiKey, string baseUrl)
    {
        _apiKey = apiKey;
        _baseUri = new Uri(baseUrl);
    }

    public LlmProvider Provider => LlmProvider.Groq;

    private IChatCompletionService Chat(string model)
    {
        var options = new OpenAIClientOptions { Endpoint = _baseUri };
        var client = new OpenAIClient(new ApiKeyCredential(_apiKey), options);
        return new OpenAIChatCompletionService(model, client);
    }

    public async Task<string> GenerateSqlAsync(string prompt, string model, CancellationToken ct = default)
    {
        var history = new ChatHistory();
        history.AddUserMessage(prompt);
        var response = await Chat(model).GetChatMessageContentAsync(history, cancellationToken: ct);
        return response.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamExplanationAsync(
        string prompt, string model, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var history = new ChatHistory();
        history.AddUserMessage(prompt);
        await foreach (var chunk in Chat(model).GetStreamingChatMessageContentsAsync(history, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
                yield return chunk.Content;
        }
    }
}
