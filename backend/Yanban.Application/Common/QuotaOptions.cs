namespace Yanban.Application.Common;

public class QuotaOptions
{
    public const string SectionName = "Quota";

    /// <summary>The largest single file, in bytes. Default 2 GiB.</summary>
    public long MaxFileBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Everything one board may hold, in bytes. Default 50 GiB.</summary>
    public long MaxBoardBytes { get; set; } = 50L * 1024 * 1024 * 1024;
}
