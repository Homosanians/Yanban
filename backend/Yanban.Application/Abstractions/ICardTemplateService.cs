using Yanban.Application.Templates;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Board-scoped card templates (ADR-12). Authorization is enforced upstream by the controller;
/// these methods only scope to the board. Creating a card *from* a template lives on
/// <see cref="ICardService"/> — it produces a card, and card creation has one home.
/// </summary>
public interface ICardTemplateService
{
    Task<IReadOnlyList<CardTemplateDto>> ListAsync(Guid boardId, CancellationToken ct);
    Task<CardTemplateDto> CreateAsync(Guid boardId, Guid userId, CreateCardTemplateRequest request, CancellationToken ct);
    Task<CardTemplateDto> UpdateAsync(Guid boardId, Guid templateId, UpdateCardTemplateRequest request, CancellationToken ct);
    Task DeleteAsync(Guid boardId, Guid templateId, CancellationToken ct);
}
