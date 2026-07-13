using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Application.Templates;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Templates;

public class CardTemplateService : ICardTemplateService
{
    private readonly YanbanDbContext _db;
    private readonly IActivityRecorder _activity;

    public CardTemplateService(YanbanDbContext db, IActivityRecorder activity)
    {
        _db = db;
        _activity = activity;
    }

    public async Task<IReadOnlyList<CardTemplateDto>> ListAsync(Guid boardId, CancellationToken ct) =>
        await _db.CardTemplates
            .AsNoTracking()
            .Where(t => t.BoardId == boardId)
            .OrderBy(t => t.Name)
            .Select(t => new CardTemplateDto(t.Id, t.BoardId, t.Name, t.Title, t.Description, t.CreatedAt))
            .ToListAsync(ct);

    public async Task<CardTemplateDto> CreateAsync(Guid boardId, Guid userId, CreateCardTemplateRequest request, CancellationToken ct)
    {
        var template = new CardTemplate
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            Name = request.Name.Trim(),
            Title = request.Title.Trim(),
            Description = request.Description,
            CreatedById = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.CardTemplates.Add(template);
        _activity.Record(boardId, ActivityAction.Created, ActivityEntityTypes.Template, template.Id,
            $"Added template \"{template.Name}\"");
        await _db.SaveChangesAsync(ct);

        return ToDto(template);
    }

    public async Task<CardTemplateDto> UpdateAsync(Guid boardId, Guid templateId, UpdateCardTemplateRequest request, CancellationToken ct)
    {
        var template = await FindAsync(boardId, templateId, ct);

        template.Name = request.Name.Trim();
        template.Title = request.Title.Trim();
        template.Description = request.Description;

        _activity.Record(boardId, ActivityAction.Updated, ActivityEntityTypes.Template, templateId,
            $"Updated template \"{template.Name}\"");
        await _db.SaveChangesAsync(ct);

        return ToDto(template);
    }

    public async Task DeleteAsync(Guid boardId, Guid templateId, CancellationToken ct)
    {
        var template = await FindAsync(boardId, templateId, ct);

        _db.CardTemplates.Remove(template);
        _activity.Record(boardId, ActivityAction.Deleted, ActivityEntityTypes.Template, templateId,
            $"Deleted template \"{template.Name}\"");
        await _db.SaveChangesAsync(ct);

        // Cards already created from this template are untouched: a template is a blueprint
        // stamped at creation, not a live link.
    }

    private async Task<CardTemplate> FindAsync(Guid boardId, Guid templateId, CancellationToken ct) =>
        await _db.CardTemplates.FirstOrDefaultAsync(t => t.Id == templateId && t.BoardId == boardId, ct)
        ?? throw new NotFoundAppException("Template not found.");

    private static CardTemplateDto ToDto(CardTemplate t) =>
        new(t.Id, t.BoardId, t.Name, t.Title, t.Description, t.CreatedAt);
}
