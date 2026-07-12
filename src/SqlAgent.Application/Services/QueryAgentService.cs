using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlAgent.Application.Options;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Application.Services;

/// <summary>
/// Orchestrates the end-to-end flow. Single clear pipeline (no MediatR):
///   introspect schema -> build prompt -> generate SQL -> validate (safety)
///   -> execute read-only -> explain (streamed).
/// </summary>
public sealed class QueryAgentService : IQueryAgentService
{
    private const int MaxRetries = 1; // one self-correction attempt on DB error

    private readonly ISchemaIntrospector _introspector;
    private readonly ISchemaCache _schemaCache;
    private readonly IAiProviderResolver _aiResolver;
    private readonly ISqlSafetyValidator _validator;
    private readonly ISqlExecutor _executor;
    private readonly PromptBuilder _prompts;
    private readonly ISqlDialectFactory _dialects;
    private readonly AgentOptions _agent;
    private readonly OllamaOptions _ollama;
    private readonly GroqOptions _groq;
    private readonly ILogger<QueryAgentService> _log;

    public QueryAgentService(
        ISchemaIntrospector introspector,
        ISchemaCache schemaCache,
        IAiProviderResolver aiResolver,
        ISqlSafetyValidator validator,
        ISqlExecutor executor,
        PromptBuilder prompts,
        ISqlDialectFactory dialects,
        IOptions<AgentOptions> agent,
        IOptions<OllamaOptions> ollama,
        IOptions<GroqOptions> groq,
        ILogger<QueryAgentService> log)
    {
        _introspector = introspector;
        _schemaCache = schemaCache;
        _aiResolver = aiResolver;
        _validator = validator;
        _executor = executor;
        _prompts = prompts;
        _dialects = dialects;
        _agent = agent.Value;
        _ollama = ollama.Value;
        _groq = groq.Value;
        _log = log;
    }

    /// <summary>Return the cached schema if present, otherwise introspect live and cache it.</summary>
    private async Task<DatabaseSchema> GetSchemaAsync(string conn, DbDialect dialect, CancellationToken ct)
    {
        if (_schemaCache.TryGet(conn, dialect, out var cached))
            return cached;
        var schema = await _introspector.IntrospectAsync(conn, dialect, ct);
        // Don't cache an empty/garbage introspection (e.g. a mismatched driver
        // that "opened" but read nothing) — it would poison later queries.
        if (schema.Tables.Count > 0)
            _schemaCache.Set(conn, dialect, schema);
        return schema;
    }

    /// <summary>Resolve the effective connection + dialect (demo DB when none given).</summary>
    private (string conn, DbDialect dialect) ResolveSource(string? connectionString, DbDialect? dialect)
    {
        var conn = string.IsNullOrWhiteSpace(connectionString)
            ? _agent.DefaultConnectionString : connectionString!;
        var d = string.IsNullOrWhiteSpace(connectionString)
            ? _agent.DefaultDialect : dialect ?? _agent.DefaultDialect;
        return (conn, d);
    }

    public async Task<SchemaLoadResult> LoadSchemaAsync(
        string? connectionString, DbDialect? dialect, bool force, CancellationToken ct = default)
    {
        var (conn, d) = ResolveSource(connectionString, dialect);
        try
        {
            if (force) _schemaCache.Invalidate(conn, d);
            var schema = await GetSchemaAsync(conn, d, ct);
            return new SchemaLoadResult
            {
                Success = true,
                TableCount = schema.Tables.Count,
                ColumnCount = schema.Tables.Sum(t => t.Columns.Count)
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Schema load failed");
            return new SchemaLoadResult { Success = false, Error = ex.Message };
        }
    }

    private (string conn, DbDialect dialect, IAiProvider ai, string model) Resolve(AskRequest req)
    {
        var conn = string.IsNullOrWhiteSpace(req.ConnectionString)
            ? _agent.DefaultConnectionString
            : req.ConnectionString!;
        var dialect = string.IsNullOrWhiteSpace(req.ConnectionString)
            ? _agent.DefaultDialect
            : req.Dialect ?? _agent.DefaultDialect;

        var provider = req.Provider ?? _agent.DefaultProvider;
        var ai = _aiResolver.Get(provider);
        var defaultModel = provider == LlmProvider.Groq ? _groq.DefaultModel : _ollama.DefaultModel;
        var model = string.IsNullOrWhiteSpace(req.Model) ? defaultModel : req.Model!;
        return (conn, dialect, ai, model);
    }

    public async Task<AskResult> AskAsync(AskRequest request, CancellationToken ct = default)
    {
        var (conn, dialectId, ai, model) = Resolve(request);
        try
        {
            var dialect = _dialects.Get(dialectId);
            var schema = await GetSchemaAsync(conn, dialectId, ct);

            var sqlPrompt = _prompts.BuildSqlPrompt(request.Question, schema, dialect, request.History);
            var rawSql = await ai.GenerateSqlAsync(sqlPrompt, model, ct);
            _log.LogInformation("Model {Model} generated SQL: {Sql}", model, rawSql);

            if (IsNoQuery(rawSql))
            {
                var chatPrompt = _prompts.BuildChatReplyPrompt(request.Question, schema, request.History);
                var reply = string.Empty;
                await foreach (var tok in ai.StreamExplanationAsync(chatPrompt, model, ct))
                    reply += tok;
                return new AskResult
                {
                    Question = request.Question, Success = true,
                    Explanation = reply, ModelUsed = model
                };
            }

            var validation = _validator.Validate(rawSql, dialect);
            if (!validation.IsValid)
                return Fail(request.Question, model, $"Rejected unsafe SQL: {validation.Reason}", rawSql);

            var result = await _executor.ExecuteReadOnlyAsync(
                conn, dialectId, validation.SafeSql!, _agent.QueryTimeoutSeconds, ct);

            var explainPrompt = _prompts.BuildExplanationPrompt(request.Question, validation.SafeSql!, result, request.History);
            var explanation = string.Empty;
            await foreach (var tok in ai.StreamExplanationAsync(explainPrompt, model, ct))
                explanation += tok;

            return new AskResult
            {
                Question = request.Question,
                GeneratedSql = validation.SafeSql,
                Result = result,
                Explanation = explanation,
                Success = true,
                ModelUsed = model
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ask failed");
            return Fail(request.Question, model, ex.Message, null);
        }
    }

    public async IAsyncEnumerable<StreamChunk> AskStreamAsync(
        AskRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (conn, dialectId, ai, model) = Resolve(request);

        // Resolve dialect.
        ISqlDialect? dialect = null;
        var stepError = TryStep(() => dialect = _dialects.Get(dialectId));
        if (stepError is not null) { yield return Err(stepError); yield break; }

        // Introspect schema.
        yield return Status("Reading database schema...");
        DatabaseSchema? schema = null;
        stepError = await TryStepAsync(async () => schema = await GetSchemaAsync(conn, dialectId, ct));
        if (stepError is not null) { yield return Err($"Could not connect / read schema: {stepError}"); yield break; }

        // Generate -> validate -> execute, retrying once if the DB rejects the
        // SQL (e.g. a hallucinated column). The DB error is fed back to the model.
        string? safeSql = null;
        QueryResult? result = null;
        string? lastError = null;
        string? prevSql = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            yield return Status(attempt == 0 ? "Generating SQL..." : "Fixing the query and retrying...");

            string? rawSql = null;
            var isRetry = attempt > 0;
            stepError = await TryStepAsync(async () =>
            {
                var prompt = isRetry
                    ? _prompts.BuildRetryPrompt(request.Question, schema!, dialect!, prevSql!, lastError!, request.History)
                    : _prompts.BuildSqlPrompt(request.Question, schema!, dialect!, request.History);
                rawSql = await ai.GenerateSqlAsync(prompt, model, ct);
                _log.LogInformation("Model {Model} generated SQL (attempt {Attempt}): {Sql}", model, attempt + 1, rawSql);
            });
            if (stepError is not null) { yield return Err($"Model error: {stepError}"); yield break; }

            // Not a data request (greeting / help / small talk) -> answer conversationally.
            if (IsNoQuery(rawSql))
            {
                yield return Status("Thinking...");
                var chatPrompt = _prompts.BuildChatReplyPrompt(request.Question, schema, request.History);
                await foreach (var tok in ai.StreamExplanationAsync(chatPrompt, model, ct))
                    yield return new StreamChunk { Type = "token", Content = tok };
                yield return new StreamChunk { Type = "done", Content = model };
                yield break;
            }

            var validation = _validator.Validate(rawSql!, dialect!);
            if (!validation.IsValid) { yield return Err($"Rejected unsafe SQL: {validation.Reason}"); yield break; }

            safeSql = validation.SafeSql!;
            yield return new StreamChunk { Type = "sql", Content = safeSql };

            yield return Status("Running query (read-only)...");
            QueryResult? attemptResult = null;
            var execError = await TryStepAsync(async () =>
                attemptResult = await _executor.ExecuteReadOnlyAsync(conn, dialectId, safeSql, _agent.QueryTimeoutSeconds, ct));

            if (execError is null) { result = attemptResult; break; }

            // Failed. Retry if we have attempts left, else surface the error.
            _log.LogWarning("Query attempt {Attempt} failed: {Error}", attempt + 1, execError);
            lastError = execError;
            prevSql = safeSql;
            if (attempt == MaxRetries) { yield return Err($"Query failed: {execError}"); yield break; }
        }

        yield return new StreamChunk { Type = "rows", Data = result };

        // Stream explanation.
        yield return Status("Explaining result...");
        var explainPrompt = _prompts.BuildExplanationPrompt(request.Question, safeSql!, result!, request.History);
        await foreach (var tok in ai.StreamExplanationAsync(explainPrompt, model, ct))
            yield return new StreamChunk { Type = "token", Content = tok };

        yield return new StreamChunk { Type = "done", Content = model };
    }

    // Runs an action and returns the error message (or null) — lets the
    // iterator yield error chunks outside any catch clause.
    private static string? TryStep(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    private static async Task<string?> TryStepAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    // True when the model signalled the input isn't a data request.
    private static bool IsNoQuery(string? rawSql)
    {
        if (string.IsNullOrWhiteSpace(rawSql)) return true;
        var cleaned = rawSql.Trim().Trim('`', '"', '\'', '.', ' ', '\r', '\n');
        return cleaned.Contains("NO_QUERY", StringComparison.OrdinalIgnoreCase);
    }

    private static StreamChunk Status(string s) => new() { Type = "status", Content = s };
    private static StreamChunk Err(string s) => new() { Type = "error", Content = s };

    private static AskResult Fail(string q, string model, string error, string? sql) => new()
    {
        Question = q,
        Success = false,
        Error = error,
        GeneratedSql = sql,
        ModelUsed = model
    };
}
