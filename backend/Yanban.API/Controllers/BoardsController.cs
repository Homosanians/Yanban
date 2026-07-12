using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Boards;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

[Route("boards")]
public class BoardsController : BoardScopedController
{
    private readonly IBoardService _boards;

    public BoardsController(YanbanDbContext db, IAuthorizationService authz, IBoardService boards)
        : base(db, authz) => _boards = boards;

    [HttpPost]
    public async Task<ActionResult<BoardDto>> Create(CreateBoardRequest request, CancellationToken ct)
    {
        var board = await _boards.CreateAsync(UserId, request, ct);
        return CreatedAtAction(nameof(Get), new { boardId = board.Id }, board);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BoardDto>>> List(CancellationToken ct)
        => Ok(await _boards.ListForUserAsync(UserId, ct));

    [HttpGet("{boardId:guid}")]
    public async Task<ActionResult<BoardDto>> Get(Guid boardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _boards.GetAsync(UserId, boardId, ct));
    }

    [HttpPut("{boardId:guid}")]
    public async Task<ActionResult<BoardDto>> Rename(Guid boardId, RenameBoardRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Manage, ct);
        return Ok(await _boards.RenameAsync(UserId, boardId, request, ct));
    }

    [HttpPost("{boardId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid boardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Manage, ct);
        await _boards.SetArchivedAsync(boardId, archived: true, ct);
        return NoContent();
    }

    [HttpPost("{boardId:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid boardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Manage, ct);
        await _boards.SetArchivedAsync(boardId, archived: false, ct);
        return NoContent();
    }

    [HttpDelete("{boardId:guid}")]
    public async Task<IActionResult> Delete(Guid boardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Delete, ct);
        await _boards.DeleteAsync(boardId, ct);
        return NoContent();
    }

    [HttpGet("{boardId:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<BoardMemberDto>>> Members(Guid boardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _boards.ListMembersAsync(boardId, ct));
    }

    [HttpPost("{boardId:guid}/members")]
    public async Task<ActionResult<BoardMemberDto>> AddMember(Guid boardId, AddMemberRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Manage, ct);
        var member = await _boards.AddMemberAsync(boardId, request, ct);
        return CreatedAtAction(nameof(Members), new { boardId }, member);
    }

    [HttpPut("{boardId:guid}/members/{userId:guid}")]
    public async Task<ActionResult<BoardMemberDto>> UpdateMember(Guid boardId, Guid userId, UpdateMemberRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Manage, ct);
        return Ok(await _boards.UpdateMemberAsync(boardId, userId, request, ct));
    }

    [HttpDelete("{boardId:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid boardId, Guid userId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Manage, ct);
        await _boards.RemoveMemberAsync(boardId, userId, ct);
        return NoContent();
    }
}
