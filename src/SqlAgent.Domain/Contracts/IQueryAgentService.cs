using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Top-level orchestrator: ties together schema introspection, prompt building,
/// SQL generation, the safety layer, execution, and explanation. The MVC
/// controller depends only on this (no MediatR — a single clear flow).
/// </summary>
public interface IQueryAgentService
{
    /// <summary>Full non-streamed flow: question in, SQL + result + explanation out.</summary>
    Task<AskResult> AskAsync(AskRequest request, CancellationToken ct = default);

    /// <summary>
    /// Streaming flow: emits status/sql/rows chunks, then streams the explanation
    /// tokens, then a done chunk. Backs the SSE endpoint.
    /// </summary>
    IAsyncEnumerable<StreamChunk> AskStreamAsync(AskRequest request, CancellationToken ct = default);
}
