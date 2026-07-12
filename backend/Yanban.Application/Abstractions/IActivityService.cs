using Yanban.Application.Activities;

namespace Yanban.Application.Abstractions;

public interface IActivityService
{
    /// <summary>
    /// Returns a board's activity newest-first. <paramref name="beforeSequence"/> is a
    /// keyset cursor: pass the smallest Sequence seen so far to fetch the next older page.
    /// </summary>
    Task<IReadOnlyList<ActivityDto>> ListAsync(Guid boardId, int limit, long? beforeSequence, CancellationToken ct);
}
