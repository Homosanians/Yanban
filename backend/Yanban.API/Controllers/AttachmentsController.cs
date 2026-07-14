using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Attachments;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

/// <summary>
/// Card attachments. Bytes never flow through the API: an upload is a two-step dance —
/// request a presigned <c>PUT</c> URL, upload straight to storage, then confirm. Reads
/// mint short-lived presigned download URLs on demand.
/// </summary>
public class AttachmentsController : BoardScopedController
{
    private readonly IAttachmentService _attachments;

    public AttachmentsController(YanbanDbContext db, IAuthorizationService authz, IAttachmentService attachments)
        : base(db, authz) => _attachments = attachments;

    [HttpPost("boards/{boardId:guid}/cards/{cardId:guid}/attachments")]
    public async Task<ActionResult<UploadTicketDto>> RequestUpload(Guid boardId, Guid cardId, CreateAttachmentRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        return Ok(await _attachments.RequestUploadAsync(boardId, cardId, request, ct));
    }

    /// <summary>
    /// Read, not Write: a viewer can see how full the board is. It is a property of the board, and
    /// hiding it from the people who can see everything on it would be an odd secret to keep.
    /// </summary>
    [HttpGet("boards/{boardId:guid}/usage")]
    public async Task<ActionResult<BoardUsageDto>> Usage(Guid boardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _attachments.GetUsageAsync(boardId, ct));
    }

    [HttpPost("boards/{boardId:guid}/cards/{cardId:guid}/attachments/{attachmentId:guid}/complete")]
    public async Task<ActionResult<AttachmentDto>> Complete(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        return Ok(await _attachments.CompleteAsync(boardId, cardId, attachmentId, ct));
    }

    [HttpGet("boards/{boardId:guid}/cards/{cardId:guid}/attachments")]
    public async Task<ActionResult<IReadOnlyList<AttachmentDto>>> List(Guid boardId, Guid cardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _attachments.ListAsync(boardId, cardId, ct));
    }

    [HttpGet("boards/{boardId:guid}/cards/{cardId:guid}/attachments/{attachmentId:guid}/download")]
    public async Task<ActionResult<DownloadUrlDto>> Download(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _attachments.GetDownloadUrlAsync(boardId, cardId, attachmentId, ct));
    }

    [HttpDelete("boards/{boardId:guid}/cards/{cardId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Delete(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        await _attachments.DeleteAsync(boardId, cardId, attachmentId, ct);
        return NoContent();
    }
}
