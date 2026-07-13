using SqlAgent.Domain.Models;

namespace SqlAgent.Domain.Contracts;

/// <summary>
/// Top-level orchestrator: ties together schema introspection, prompt building,
/// SQL generation, the safety layer, execution, and explanation. The MVC
/// controller depends only on this (no MediatR — a single clear flow).
/// </summary>
public interface IQueryAgentService
{
    /// <summary>
    /// The one answering flow: emits status/sql/rows chunks, then streams the
    /// explanation tokens, then a done chunk. Backs the SSE endpoint.
    /// </summary>
    IAsyncEnumerable<StreamChunk> AskStreamAsync(AskRequest request, CancellationToken ct = default);

    /// <summary>
    /// Introspect and cache the full schema for a data source (called on Save and
    /// by the "refresh schema" button). <paramref name="force"/> re-reads even if
    /// cached. Returns the table/column count summary, or throws on failure.
    /// </summary>
    Task<SchemaLoadResult> LoadSchemaAsync(
        string? connectionString, DbDialect? dialect, bool force, CancellationToken ct = default);
}
