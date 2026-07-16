using System.ComponentModel.DataAnnotations;

namespace Yanban.Application.Attachments;

/// <summary>Client's declared metadata when requesting an upload slot.</summary>
public record CreateAttachmentRequest(
    [Required, MaxLength(255)] string FileName,
    [Required, MaxLength(255)] string ContentType,
    [Range(1, long.MaxValue)] long SizeBytes);

/// <summary>
/// Instructions for the direct-to-storage upload. The client must <c>PUT</c> the bytes
/// to <see cref="UploadUrl"/> with the given <see cref="ContentType"/> header, then call
/// the complete endpoint.
/// </summary>
public record UploadTicketDto(Guid AttachmentId, string Method, string UploadUrl, string ContentType, DateTimeOffset ExpiresAt);

/// <summary>A ready attachment's metadata (no URL; download URLs are minted on demand).</summary>
public record AttachmentDto(
    Guid Id, Guid CardId, string FileName, string ContentType, long SizeBytes,
    Guid UploadedById, DateTimeOffset CreatedAt);

public record DownloadUrlDto(string DownloadUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// What a board is holding, and what it is allowed to hold. <paramref name="UsedBytes"/> counts only
/// completed attachments; a half-finished upload holds a reservation against the quota but is not
/// shown in the usage bar.
/// </summary>
public record BoardUsageDto(long UsedBytes, long MaxBoardBytes, long MaxFileBytes, int FileCount);
