using Yanban.Domain.Enums;

namespace Yanban.Domain.Entities;

/// <summary>
/// A file attached to a card. The bytes live in S3-compatible object storage, never
/// in Postgres and never proxied through the API — this row is the metadata plus the
/// storage key used to mint presigned upload/download URLs.
/// </summary>
public class Attachment
{
    public Guid Id { get; set; }

    public Guid CardId { get; set; }
    public Card Card { get; set; } = null!;

    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;

    /// <summary>Client-declared size, verified against the stored object on completion.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Opaque object-storage key (e.g. <c>attachments/{id}</c>).</summary>
    public string StorageKey { get; set; } = null!;

    public AttachmentStatus Status { get; set; }

    public Guid UploadedById { get; set; }
    public User UploadedBy { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
