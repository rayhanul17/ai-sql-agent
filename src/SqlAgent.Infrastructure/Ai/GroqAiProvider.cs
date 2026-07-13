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

    public async Task<string> GenerateSqlAsync(string prompt, string model, string? systemMessage = null, CancellationToken ct = default)
    {
        var history = BuildHistory(prompt, systemMessage);
        var response = await Chat(model).GetChatMessageContentAsync(history, cancellationToken: ct);
        // Reasoning models (e.g. qwen3) prepend a <think>...</think> block; strip it
        // so the caller gets only the SQL / intent label, not the chain-of-thought.
        return ThinkFilter.StripString(response.Content);
    }

    public IAsyncEnumerable<string> StreamExplanationAsync(
        string prompt, string model, string? systemMessage = null, CancellationToken ct = default)
    {
        return ThinkFilter.StripAsync(Raw(prompt, model, systemMessage, ct), ct);
    }

    private async IAsyncEnumerable<string> Raw(
        string prompt, string model, string? systemMessage, [EnumeratorCancellation] CancellationToken ct)
    {
        var history = BuildHistory(prompt, systemMessage);
        await foreach (var chunk in Chat(model).GetStreamingChatMessageContentsAsync(history, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
                yield return chunk.Content;
        }
    }

    // A system message (when given) carries the stable agent rules as their own
    // role; the task prompt stays the user message.
    private static ChatHistory BuildHistory(string prompt, string? systemMessage)
    {
        var history = new ChatHistory();
        if (!string.IsNullOrWhiteSpace(systemMessage))
            history.AddSystemMessage(systemMessage);
        history.AddUserMessage(prompt);
        return history;
    }
}
