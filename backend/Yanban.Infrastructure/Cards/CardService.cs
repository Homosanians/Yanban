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
            (uint)_db.Entry(card).Property(XminProperty).CurrentValue!);
}
