using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Templates;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

/// <summary>
/// Board-scoped card templates. Curating them is an Editor's job (<c>Write</c>), not board
/// administration; a template is a card-shaped artifact, not board configuration.
/// </summary>
public class CardTemplatesController : BoardScopedController
{
    private readonly ICardTemplateService _templates;

    public CardTemplatesController(YanbanDbContext db, IAuthorizationService authz, ICardTemplateService templates)
        : base(db, authz) => _templates = templates;

    [HttpGet("boards/{boardId:guid}/templates")]
    public async Task<ActionResult<IReadOnlyList<CardTemplateDto>>> List(Guid boardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _templates.ListAsync(boardId, ct));
    }

    [HttpPost("boards/{boardId:guid}/templates")]
    public async Task<ActionResult<CardTemplateDto>> Create(Guid boardId, CreateCardTemplateRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        var template = await _templates.CreateAsync(boardId, UserId, request, ct);
        return CreatedAtAction(nameof(List), new { boardId }, template);
    }

    [HttpPut("boards/{boardId:guid}/templates/{templateId:guid}")]
    public async Task<ActionResult<CardTemplateDto>> Update(
        Guid boardId, Guid templateId, UpdateCardTemplateRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        return Ok(await _templates.UpdateAsync(boardId, templateId, request, ct));
    }

    [HttpDelete("boards/{boardId:guid}/templates/{templateId:guid}")]
    public async Task<IActionResult> Delete(Guid boardId, Guid templateId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        await _templates.DeleteAsync(boardId, templateId, ct);
        return NoContent();
    }
}
