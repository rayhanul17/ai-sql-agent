using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.ChatCompletion;
using OllamaSharp;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

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

    public LlmProvider Provider => LlmProvider.Ollama;

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
