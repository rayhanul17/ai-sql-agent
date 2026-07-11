using System.Collections.Concurrent;
using SqlAgent.Domain.Contracts;
using SqlAgent.Domain.Models;

namespace SqlAgent.Infrastructure.Database;

/// <summary>
/// Simple in-memory schema cache keyed by connection string + dialect.
/// Sufficient for this single-user app; a distributed cache could replace it
/// without touching callers (they depend on <see cref="ISchemaCache"/>).
/// </summary>
public sealed class SchemaCache : ISchemaCache
{
    private readonly ConcurrentDictionary<string, DatabaseSchema> _cache = new();

    private static string Key(string connectionString, DbDialect dialect) =>
        $"{dialect}::{connectionString}";

    public bool TryGet(string connectionString, DbDialect dialect, out DatabaseSchema schema) =>
        _cache.TryGetValue(Key(connectionString, dialect), out schema!);

    public void Set(string connectionString, DbDialect dialect, DatabaseSchema schema) =>
        _cache[Key(connectionString, dialect)] = schema;

    public void Invalidate(string connectionString, DbDialect dialect) =>
        _cache.TryRemove(Key(connectionString, dialect), out _);
}
