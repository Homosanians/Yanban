using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Lists;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

[Route("boards/{boardId:guid}/lists")]
public class ListsController : BoardScopedController
{
    private readonly IListService _lists;

    public ListsController(YanbanDbContext db, IAuthorizationService authz, IListService lists)
        : base(db, authz) => _lists = lists;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ListDto>>> List(Guid boardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _lists.ListAsync(boardId, ct));
    }

    [HttpPost]
    public async Task<ActionResult<ListDto>> Create(Guid boardId, CreateListRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        var list = await _lists.CreateAsync(boardId, request, ct);
        return CreatedAtAction(nameof(List), new { boardId }, list);
    }

    [HttpPut("{listId:guid}")]
    public async Task<ActionResult<ListDto>> Rename(Guid boardId, Guid listId, RenameListRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        return Ok(await _lists.RenameAsync(boardId, listId, request, ct));
    }

    [HttpDelete("{listId:guid}")]
    public async Task<IActionResult> Delete(Guid boardId, Guid listId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        await _lists.DeleteAsync(boardId, listId, ct);
        return NoContent();
    }
}
