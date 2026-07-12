namespace Yanban.Application.Abstractions;

/// <summary>
/// Small caching port. In-memory for the MVP; a Redis implementation can be
/// swapped in behind this interface without touching callers.
/// </summary>
public interface ICacheService
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken ct = default);
    void Remove(string key);
}
