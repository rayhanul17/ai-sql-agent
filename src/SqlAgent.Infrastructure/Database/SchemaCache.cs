using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SqlAgent.Application.Options;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Database;

/// <summary>
/// Simple in-memory schema cache keyed by connection string + dialect.
/// Entries expire after <see cref="AgentOptions.SchemaCacheTtlMinutes"/> so a
/// schema that changed after it was cached is eventually re-read; the query path
/// also force-invalidates on a DB error for immediate self-healing.
/// Sufficient for this single-user app; a distributed cache could replace it
/// without touching callers (they depend on <see cref="ISchemaCache"/>).
/// </summary>
public sealed class SchemaCache : ISchemaCache
{
    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    private readonly TimeSpan _ttl;

    public SchemaCache(IOptions<AgentOptions> options)
    {
        var minutes = options.Value.SchemaCacheTtlMinutes;
        // 0 (or negative) disables time-based expiry.
        _ttl = minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.Zero;
    }

    private static string Key(string connectionString, DbDialect dialect) =>
        $"{dialect}::{connectionString}";

    public bool TryGet(string connectionString, DbDialect dialect, out DatabaseSchema schema)
    {
        var key = Key(connectionString, dialect);
        if (_cache.TryGetValue(key, out var entry))
        {
            if (!entry.IsExpired(_ttl))
            {
                schema = entry.Schema;
                return true;
            }
            // Stale — drop it so the caller re-introspects.
            _cache.TryRemove(key, out _);
        }
        schema = null!;
        return false;
    }

    public void Set(string connectionString, DbDialect dialect, DatabaseSchema schema) =>
        _cache[Key(connectionString, dialect)] = new Entry(schema, DateTimeOffset.UtcNow);

    public void Invalidate(string connectionString, DbDialect dialect) =>
        _cache.TryRemove(Key(connectionString, dialect), out _);

    private readonly record struct Entry(DatabaseSchema Schema, DateTimeOffset CachedAt)
    {
        public bool IsExpired(TimeSpan ttl) =>
            ttl > TimeSpan.Zero && DateTimeOffset.UtcNow - CachedAt > ttl;
    }
}
