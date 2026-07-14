using Yanban.Application.Attachments;

namespace Yanban.Application.Abstractions;

public interface IAttachmentService
{
    /// <summary>Creates a pending attachment and returns a presigned upload URL.</summary>
    Task<UploadTicketDto> RequestUploadAsync(Guid boardId, Guid cardId, CreateAttachmentRequest request, CancellationToken ct);

    /// <summary>Verifies the object was uploaded (and matches the declared size), then marks it ready.</summary>
    Task<AttachmentDto> CompleteAsync(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct);

    Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid boardId, Guid cardId, CancellationToken ct);

    /// <summary>Mints a short-lived presigned download URL for a ready attachment.</summary>
    Task<DownloadUrlDto> GetDownloadUrlAsync(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct);

    Task DeleteAsync(Guid boardId, Guid cardId, Guid attachmentId, CancellationToken ct);

    /// <summary>What the board is storing, against what it is allowed to store.</summary>
    Task<BoardUsageDto> GetUsageAsync(Guid boardId, CancellationToken ct);
}
