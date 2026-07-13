namespace Yanban.Domain.Enums;

/// <summary>
/// Lifecycle of an attachment. Because the backend never sees the bytes (they go
/// straight to object storage via a presigned URL), an attachment starts
/// <see cref="Pending"/> when its upload URL is issued and only becomes
/// <see cref="Ready"/> once the client confirms and the object is verified present.
/// </summary>
public enum AttachmentStatus
{
    Pending,
    Ready
}
