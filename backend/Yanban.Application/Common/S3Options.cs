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

    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "yanban-attachments";

    /// <summary>How long issued presigned upload/download URLs stay valid.</summary>
    public int PresignExpiryMinutes { get; set; } = 15;

    /// <summary>Upper bound on a single attachment's declared size (default 10 MiB).</summary>
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}
