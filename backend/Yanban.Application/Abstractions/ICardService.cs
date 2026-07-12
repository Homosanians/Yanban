using Yanban.Application.Cards;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Card operations, scoped to a board (IDOR-safe). Updates take the expected
/// <c>xmin</c> version and enforce it as an optimistic-concurrency precondition.
/// </summary>
public interface ICardService
{
    Task<IReadOnlyList<CardDto>> ListAsync(Guid boardId, Guid listId, CancellationToken ct);
    Task<CardDto> GetAsync(Guid boardId, Guid cardId, CancellationToken ct);
    Task<CardDto> CreateAsync(Guid boardId, Guid listId, Guid userId, CreateCardRequest request, CancellationToken ct);
    Task<CardDto> UpdateAsync(Guid boardId, Guid cardId, uint expectedVersion, UpdateCardRequest request, CancellationToken ct);
    Task DeleteAsync(Guid boardId, Guid cardId, CancellationToken ct);
}
