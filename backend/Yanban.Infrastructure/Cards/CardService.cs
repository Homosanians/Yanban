using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Cards;
using Yanban.Application.Common;
using Yanban.Domain.Entities;
using Yanban.Domain.Ordering;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Cards;

public class CardService : ICardService
{
    private const string XminProperty = "xmin";

    private readonly YanbanDbContext _db;

    public CardService(YanbanDbContext db) => _db = db;

    public async Task<IReadOnlyList<CardDto>> ListAsync(Guid boardId, Guid listId, CancellationToken ct)
    {
        if (!await _db.Lists.AnyAsync(l => l.Id == listId && l.BoardId == boardId, ct))
            throw new NotFoundAppException("List not found.");

        var cards = await _db.Cards
            .Where(c => c.ListId == listId)
            .OrderBy(c => c.Rank)
            .ToListAsync(ct);

        return cards.Select(ToDto).ToList();
    }

    public async Task<CardDto> GetAsync(Guid boardId, Guid cardId, CancellationToken ct) =>
        ToDto(await FindCardAsync(boardId, cardId, ct));

    public async Task<CardDto> CreateAsync(Guid boardId, Guid listId, Guid userId, CreateCardRequest request, CancellationToken ct)
    {
        if (!await _db.Lists.AnyAsync(l => l.Id == listId && l.BoardId == boardId, ct))
            throw new NotFoundAppException("List not found.");

        var lastRank = await _db.Cards
            .Where(c => c.ListId == listId)
            .OrderByDescending(c => c.Rank)
            .Select(c => c.Rank)
            .FirstOrDefaultAsync(ct);

        var card = new Card
        {
            Id = Guid.NewGuid(),
            ListId = listId,
            Title = request.Title.Trim(),
            Description = request.Description,
            DueDate = request.DueDate,
            Rank = Rank.After(lastRank),
            CreatedById = userId
        };
        _db.Cards.Add(card);
        await _db.SaveChangesAsync(ct);

        return ToDto(card);
    }

    public async Task<CardDto> UpdateAsync(Guid boardId, Guid cardId, uint expectedVersion, UpdateCardRequest request, CancellationToken ct)
    {
        var card = await FindCardAsync(boardId, cardId, ct);

        // Crux of the optimistic-concurrency check: force the UPDATE to target the
        // client's version (UPDATE ... WHERE xmin = expectedVersion). The freshly
        // loaded OriginalValue is the *current* xmin, so without this override EF
        // would compare the row against itself and the precondition could never fail.
        _db.Entry(card).Property(XminProperty).OriginalValue = expectedVersion;

        card.Title = request.Title.Trim();
        card.Description = request.Description;
        card.DueDate = request.DueDate;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PreconditionFailedAppException("The card was modified by someone else. Reload and retry.");
        }

        return ToDto(card);
    }

    public async Task<CardDto> MoveAsync(Guid boardId, Guid cardId, MoveCardRequest request, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Lock the target list row — the ordering mutex. Every mover into this list
        // takes this lock first, so concurrent moves serialize and can't compute the
        // same midpoint. Locking only the target is enough: moving a card *out* of its
        // source leaves the source's remaining ranks untouched. The `AND board_id` both
        // scopes the move to this board (the target list id is client-supplied) and
        // validates it in the same round-trip. `lists` has no concurrency token, so
        // SELECT * is safe for FromSql; ToListAsync runs the statement verbatim (no
        // subquery wrapping) so FOR UPDATE stays at the top level.
        var locked = await _db.Lists
            .FromSql($"SELECT * FROM lists WHERE id = {request.TargetListId} AND board_id = {boardId} FOR UPDATE")
            .ToListAsync(ct);
        if (locked.Count == 0)
            throw new NotFoundAppException("List not found.");

        // Decision reads happen *after* the lock, so a mover that was blocked wakes up
        // and recomputes against the now-current state rather than a stale snapshot.
        var card = await FindCardAsync(boardId, cardId, ct);

        var others = await _db.Cards
            .Where(c => c.ListId == request.TargetListId && c.Id != cardId)
            .OrderBy(c => c.Rank)
            .ToListAsync(ct);

        var position = Math.Clamp(request.Position, 0, others.Count);
        var left = position > 0 ? others[position - 1].Rank : null;
        var right = position < others.Count ? others[position].Rank : null;

        card.ListId = request.TargetListId;
        if (Rank.TryBetween(left, right, out var rank))
        {
            card.Rank = rank;
        }
        else
        {
            // The neighbours are adjacent with no encodable rank between them (rare —
            // Gap allows ~16 bisections at one slot). Re-space the whole target list at
            // full Gap intervals with the moved card inserted at its position.
            var ordered = new List<Card>(others);
            ordered.Insert(position, card);
            string? previous = null;
            foreach (var c in ordered)
            {
                c.Rank = Rank.After(previous);
                previous = c.Rank;
            }
        }

        // No explicit If-Match on move; the card's xmin token still guards against a
        // concurrent edit slipping in, surfacing as 409 via the exception middleware.
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToDto(card);
    }

    public async Task<CardDto> AssignAsync(Guid boardId, Guid cardId, Guid? assigneeId, CancellationToken ct)
    {
        var card = await FindCardAsync(boardId, cardId, ct);

        if (assigneeId is Guid userId)
        {
            // A card can only be assigned to a member of its board. Same 400 whether the
            // user is a non-member or doesn't exist, so this can't be used to probe users.
            var isMember = await _db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == userId, ct);
            if (!isMember)
                throw new ValidationAppException("The assignee must be a member of the board.");
        }

        card.AssigneeId = assigneeId;
        // No client If-Match here (assignment is a low-contention scalar), but the card's
        // xmin token still guards the tracked save against a lost update -> rare 409.
        await _db.SaveChangesAsync(ct);

        return ToDto(card);
    }

    public async Task DeleteAsync(Guid boardId, Guid cardId, CancellationToken ct)
    {
        var card = await FindCardAsync(boardId, cardId, ct);
        _db.Cards.Remove(card);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Card> FindCardAsync(Guid boardId, Guid cardId, CancellationToken ct) =>
        await _db.Cards
            .Include(c => c.List)
            .FirstOrDefaultAsync(c => c.Id == cardId && c.List.BoardId == boardId, ct)
        ?? throw new NotFoundAppException("Card not found.");

    private CardDto ToDto(Card card) =>
        new(card.Id, card.ListId, card.Title, card.Description, card.DueDate, card.Rank,
            (uint)_db.Entry(card).Property(XminProperty).CurrentValue!, card.AssigneeId);
}
