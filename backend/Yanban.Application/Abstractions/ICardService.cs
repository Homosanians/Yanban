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

    /// <summary>
    /// Moves a card to a list/position. Serializes concurrent moves into the same list
    /// with a row lock on the target list (pessimistic), as opposed to the optimistic
    /// <c>xmin</c> concurrency used by <see cref="UpdateAsync"/>.
    /// </summary>
    Task<CardDto> MoveAsync(Guid boardId, Guid cardId, MoveCardRequest request, CancellationToken ct);

    /// <summary>Sets or clears the card's assignee; a non-null assignee must be a board member.</summary>
    Task<CardDto> AssignAsync(Guid boardId, Guid cardId, Guid? assigneeId, CancellationToken ct);

    Task DeleteAsync(Guid boardId, Guid cardId, CancellationToken ct);
}
