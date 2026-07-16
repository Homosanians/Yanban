namespace Yanban.Application.Abstractions;

/// <summary>
/// Thin seam over S3-compatible object storage. The API never streams bytes itself:
/// it hands clients short-lived presigned URLs and only performs control-plane
/// operations (ensure bucket, verify/delete objects) here. Presign calls are local
/// signature computation, no network round-trip.
/// </summary>
public interface IObjectStorage
{
    /// <summary>Creates the configured bucket if it does not yet exist.</summary>
    Task EnsureBucketAsync(CancellationToken ct);

    /// <summary>A presigned <c>PUT</c> URL. The signature pins <paramref name="contentType"/>,
    /// so the client's upload must send a matching <c>Content-Type</c> header.</summary>
    (string Url, DateTimeOffset ExpiresAt) CreateUploadUrl(string key, string contentType, TimeSpan expiry);

    /// <summary>A presigned <c>GET</c> URL that downloads as <paramref name="fileName"/>.</summary>
    (string Url, DateTimeOffset ExpiresAt) CreateDownloadUrl(string key, string fileName, string contentType, TimeSpan expiry);

    /// <summary>The stored object's size, or null if it does not exist (used to verify an upload).</summary>
    Task<long?> TryGetObjectSizeAsync(string key, CancellationToken ct);

    /// <summary>Deletes the object; a no-op if it is already gone (delete is idempotent).</summary>
    Task DeleteAsync(string key, CancellationToken ct);
}
