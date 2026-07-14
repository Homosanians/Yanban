using Yanban.Application.Activities;

namespace Yanban.Application.Abstractions;

public interface IActivityService
{
    /// <summary>
    /// Returns a board's activity newest-first, narrowed by <paramref name="query"/>. The keyset
    /// cursor survives every filter: paging deeper into a *search* works the same way as paging
    /// deeper into the plain feed.
    /// </summary>
    Task<IReadOnlyList<ActivityDto>> ListAsync(Guid boardId, ActivityQuery query, CancellationToken ct);
}
