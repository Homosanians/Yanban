namespace Yanban.Application.Common;

public class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Origins allowed to call the API cross-origin, outside Development. Empty by default, since the
    /// app is served same-origin through the proxy. In Development every origin is reflected.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
