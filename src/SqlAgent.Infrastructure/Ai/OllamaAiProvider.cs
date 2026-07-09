using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.ChatCompletion;
using OllamaSharp;
using SqlAgent.Domain.Contracts;

namespace SqlAgent.Infrastructure.Ai;

/// <summary>
/// LLM provider backed by Semantic Kernel's Ollama connector (the .NET
/// equivalent of LangChain). A fresh chat-completion service is built per
/// call so the model can be switched at runtime (per request) without
/// rebuilding a global kernel.
/// </summary>
public sealed class OllamaAiProvider : IAiProvider
{
    private readonly Uri _baseUri;

    public OllamaAiProvider(Uri baseUri) => _baseUri = baseUri;

    // Semantic Kernel is the orchestrator. SK now sits on the
    // Microsoft.Extensions.AI abstraction (IChatClient), so the recommended
    // pattern is: build the Ollama client, then adapt it to SK's
    // IChatCompletionService via AsChatCompletionService(). (The old
    // OllamaChatCompletionService type is deprecated.) Model is passed here
    // so it can be switched at runtime, per request.
    private IChatCompletionService Chat(string model)
    {
        var client = new OllamaApiClient(_baseUri, model);
        return ((IChatClient)client).AsChatCompletionService();
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
