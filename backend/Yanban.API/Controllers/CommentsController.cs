using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Comments;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

/// <summary>
/// Comments on a card. All mutations gate on <see cref="BoardPermission.Write"/> so an
/// archived board is read-only for comments too; identity then narrows it further:
/// only the author edits, only the author or a board admin deletes.
/// </summary>
[Route("boards/{boardId:guid}/cards/{cardId:guid}/comments")]
public class CommentsController : BoardScopedController
{
    private readonly ICommentService _comments;

    public CommentsController(YanbanDbContext db, IAuthorizationService authz, ICommentService comments)
        : base(db, authz) => _comments = comments;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> List(Guid boardId, Guid cardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _comments.ListAsync(boardId, cardId, ct));
    }

    [HttpPost]
    public async Task<ActionResult<CommentDto>> Create(Guid boardId, Guid cardId, CreateCommentRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        var comment = await _comments.CreateAsync(boardId, cardId, UserId, request, ct);
        return CreatedAtAction(nameof(List), new { boardId, cardId }, comment);
    }

    [HttpPut("{commentId:guid}")]
    public async Task<ActionResult<CommentDto>> Update(Guid boardId, Guid cardId, Guid commentId, UpdateCommentRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        return Ok(await _comments.UpdateAsync(boardId, cardId, commentId, UserId, request, ct));
    }

    [HttpDelete("{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid boardId, Guid cardId, Guid commentId, CancellationToken ct)
    {
        var board = await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        var canModerate = await HasPermissionAsync(board, BoardPermission.Manage);
        await _comments.DeleteAsync(boardId, cardId, commentId, UserId, canModerate, ct);
        return NoContent();
    }
}
