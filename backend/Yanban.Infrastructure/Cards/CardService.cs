using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Cards;
using Yanban.Application.Common;
using Yanban.Application.Notifications;
using Yanban.Application.Templates;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Domain.Ordering;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Cards;

public class CardService : ICardService
{
    private const string XminProperty = "xmin";

    private readonly YanbanDbContext _db;
    private readonly IActivityRecorder _activity;
    private readonly INotificationOutbox _outbox;
    private readonly ICurrentUser _currentUser;

    public CardService(
        YanbanDbContext db,
        IActivityRecorder activity,
        INotificationOutbox outbox,
        ICurrentUser currentUser)
    {
        _db = db;
        _activity = activity;
        _outbox = outbox;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Queues a card notification with everything the worker needs to render it. The worker must not
    /// re-read the card later, since the title may have changed by then.
    ///
    /// <para>Like every enqueue, this only calls <c>Add</c>; the caller's <c>SaveChanges</c> is what
    /// commits it.</para>
    /// </summary>
    private async Task NotifyAsync(
        NotificationType type,
        Guid recipientId,
        Guid boardId,
        Card card,
        CancellationToken ct,
        string? listName = null,
        string? commentBody = null)
    {
        var boardName = await _db.Boards
            .AsNoTracking()
            .Where(b => b.Id == boardId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(ct) ?? "a board";

        var actorName = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == _currentUser.UserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct) ?? "Someone";

        await _outbox.EnqueueAsync(
            type,
            recipientId,
            boardId,
            new CardNotificationPayload(actorName, boardName, card.Title, card.Id, listName, commentBody),
            ct);
    }

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

    public Task<CardDto> CreateAsync(Guid boardId, Guid listId, Guid userId, CreateCardRequest request, CancellationToken ct) =>
        AppendCardAsync(boardId, listId, userId, request.Title, request.Description, request.DueDate, ct);

    public async Task<CardDto> CreateFromTemplateAsync(
        Guid boardId, Guid listId, Guid userId, CreateCardFromTemplateRequest request, CancellationToken ct)
    {
        // Scoped to the board, so a template id from another board is a 404, not a leak.
        var template = await _db.CardTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId && t.BoardId == boardId, ct)
            ?? throw new NotFoundAppException("Template not found.");

        // Copy the template's text onto the card now. Cards don't track which template they came
        // from, so editing a template later won't rewrite existing cards.
        var title = string.IsNullOrWhiteSpace(request.Title) ? template.Title : request.Title;
        return await AppendCardAsync(boardId, listId, userId, title, template.Description, dueDate: null, ct);
    }

    /// <summary>Adds a card at the end of a list.</summary>
    private async Task<CardDto> AppendCardAsync(
        Guid boardId, Guid listId, Guid userId, string title, string? description, DateTimeOffset? dueDate, CancellationToken ct)
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
            Title = title.Trim(),
            Description = description,
            DueDate = dueDate,
            Rank = Rank.After(lastRank),
            CreatedById = userId
        };
        _db.Cards.Add(card);
        _activity.Record(boardId, ActivityAction.Created, ActivityEntityTypes.Card, card.Id, $"Added card \"{card.Title}\"");
        await _db.SaveChangesAsync(ct);

        return ToDto(card);
    }

    public async Task<CardDto> UpdateAsync(Guid boardId, Guid cardId, uint expectedVersion, UpdateCardRequest request, CancellationToken ct)
    {
        var card = await FindCardAsync(boardId, cardId, ct);

        // Force the UPDATE to target the client's version (UPDATE ... WHERE xmin =
        // expectedVersion). The freshly loaded OriginalValue holds the current xmin,
        // so without this override EF compares the row against itself and the
        // precondition can never fail.
        _db.Entry(card).Property(XminProperty).OriginalValue = expectedVersion;

        // Capture the old title before overwriting. Only record it when the title actually
        // changed; an edit that touches only the description isn't a rename, and logging an
        // unchanged title just adds noise to the audit log.
        var oldTitle = card.Title;
        var newTitle = request.Title.Trim();
        var renamed = !string.Equals(oldTitle, newTitle, StringComparison.Ordinal);

        card.Title = newTitle;
        card.Description = request.Description;
        card.DueDate = request.DueDate;

        // Recorded in the same SaveChanges, so if the xmin precondition fails below,
        // the audit row is rolled back with the update. A rejected edit leaves no trace.
        _activity.Record(boardId, ActivityAction.Updated, ActivityEntityTypes.Card, cardId,
            $"Updated \"{card.Title}\"",
            oldValue: renamed ? oldTitle : null,
            newValue: renamed ? newTitle : null);

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

        // Lock the target list row; this is the ordering mutex. Every mover into this
        // list takes the lock first, so concurrent moves serialize and can't compute
        // the same midpoint. Locking only the target is enough, since moving a card out
        // of its source leaves the source's ranks untouched. The AND board_id scopes the
        // move to this board (the target list id is client-supplied) and validates it in
        // the same round-trip. lists has no concurrency token, so SELECT * is safe for
        // FromSql, and ToListAsync runs the statement verbatim (no subquery wrapping) so
        // FOR UPDATE stays at the top level.
        var locked = await _db.Lists
            .FromSql($"SELECT * FROM lists WHERE id = {request.TargetListId} AND board_id = {boardId} FOR UPDATE")
            .ToListAsync(ct);
        if (locked.Count == 0)
            throw new NotFoundAppException("List not found.");

        // Read the state after taking the lock, so a mover that was blocked recomputes
        // against current state rather than a stale snapshot.
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
            // The neighbours are adjacent with no encodable rank between them. Rare,
            // since Gap allows about 16 bisections in one slot. Re-space the whole target
            // list at full Gap intervals with the moved card inserted at its position.
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
        _activity.Record(boardId, ActivityAction.Moved, ActivityEntityTypes.Card, cardId, $"Moved \"{card.Title}\"");

        // Only the assignee cares that their card moved, and only when someone else moved it.
        // The outbox drops any message whose recipient is the actor.
        if (card.AssigneeId is Guid owner)
            await NotifyAsync(NotificationType.AssignedCardMoved, owner, boardId, card, ct, locked[0].Name);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToDto(card);
    }

    public async Task<CardDto> AssignAsync(Guid boardId, Guid cardId, Guid? assigneeId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Lock the board before checking membership, so the check and the write can't straddle
        // a concurrent removal. Without it, RemoveMemberAsync could delete the membership and
        // sweep its assignments between the check and the write, stranding the card on a
        // non-member. Both take the board lock before any card, so they serialize without deadlock.
        await _db.Boards.FromSql($"SELECT * FROM boards WHERE id = {boardId} FOR UPDATE").ToListAsync(ct);

        var card = await FindCardAsync(boardId, cardId, ct);

        if (assigneeId is Guid userId)
        {
            // A card can only be assigned to a member of its board. Same 400 whether the
            // user is a non-member or doesn't exist, so this can't be used to probe users.
            var isMember = await _db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == userId, ct);
            if (!isMember)
                throw new ValidationAppException("The assignee must be a member of the board.");
        }

        // Capture the previous assignee before overwriting, so we can notify whoever lost the card.
        var previousAssignee = card.AssigneeId;

        card.AssigneeId = assigneeId;
        _activity.Record(boardId, ActivityAction.Assigned, ActivityEntityTypes.Card, cardId,
            assigneeId is null ? $"Unassigned \"{card.Title}\"" : $"Assigned \"{card.Title}\"");

        // Queued in the same SaveChanges: if the save below loses the xmin check, the mail is
        // rolled back with it, so no one is notified about an assignment that didn't commit.
        if (assigneeId is Guid newAssignee && newAssignee != previousAssignee)
            await NotifyAsync(NotificationType.CardAssigned, newAssignee, boardId, card, ct);

        if (previousAssignee is Guid lost && lost != assigneeId)
            await NotifyAsync(NotificationType.CardUnassigned, lost, boardId, card, ct);
        // No client If-Match here (assignment is a low-contention scalar), but the card's
        // xmin token still guards the tracked save against a lost update, giving a rare 409.
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToDto(card);
    }

    public async Task DeleteAsync(Guid boardId, Guid cardId, CancellationToken ct)
    {
        var card = await FindCardAsync(boardId, cardId, ct);
        _db.Cards.Remove(card);
        _activity.Record(boardId, ActivityAction.Deleted, ActivityEntityTypes.Card, cardId, $"Deleted \"{card.Title}\"");
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
