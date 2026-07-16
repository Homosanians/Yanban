namespace Yanban.Application.Common;

/// <summary>
/// Object-storage (S3-compatible; MinIO in dev) configuration. Bound from the "S3"
/// configuration section.
/// </summary>
public class S3Options
{
    public const string SectionName = "S3";

    /// <summary>Base URL the API uses to talk to storage, e.g. <c>http://localhost:9000</c>.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// Base URL a browser uses to reach storage, e.g. <c>http://localhost:9000</c> while the API
    /// talks to <c>http://minio:9000</c> inside the Compose network. Presigned URLs are minted
    /// against this, because the host is part of the signature and cannot be rewritten afterwards.
    /// Empty means the browser and the API reach storage identically.
    /// </summary>
    public string PublicEndpoint { get; set; } = "";

    /// <summary>The endpoint presigned URLs are signed for: <see cref="PublicEndpoint"/> if set, else <see cref="Endpoint"/>.</summary>
    public string PresignEndpoint => string.IsNullOrWhiteSpace(PublicEndpoint) ? Endpoint : PublicEndpoint;

    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "yanban-attachments";

    /// <summary>How long issued presigned upload/download URLs stay valid.</summary>
    public int PresignExpiryMinutes { get; set; } = 15;

    // File-size limits live in QuotaOptions behind IBoardQuotaPolicy: how large a file may be is a
    // policy about a board, decided alongside the board total, not a fact about the storage backend.
}
