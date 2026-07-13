using Yanban.Application.Search;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Full-text search over a board's cards (ADR-12). Authorization is enforced upstream by the
/// controller (<c>RequireBoardAsync(Read)</c>); this only scopes results to the board.
/// </summary>
public interface ICardSearchService
{
    /// <summary>
    /// Returns the <paramref name="limit"/> best matches for <paramref name="query"/>, most
    /// relevant first. Results are ranked, not paged — refine the query rather than page it.
    /// </summary>
    Task<IReadOnlyList<CardSearchHit>> SearchAsync(Guid boardId, string query, int limit, CancellationToken ct);
}
