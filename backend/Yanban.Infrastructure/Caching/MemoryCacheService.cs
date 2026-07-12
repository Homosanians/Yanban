using Microsoft.Extensions.Caching.Memory;
using Yanban.Application.Abstractions;

namespace Yanban.Infrastructure.Caching;

/// <summary>In-memory ICacheService. Swap for Redis later.</summary>
public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    public MemoryCacheService(IMemoryCache cache) => _cache = cache;

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var cached) && cached is T typed)
            return typed;

        var created = await factory();
        _cache.Set(key, created, ttl);
        return created;
    }

    public void Remove(string key) => _cache.Remove(key);
}
