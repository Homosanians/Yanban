namespace Yanban.Application.Common;

public class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Origins allowed to call the API cross-origin, outside Development. Empty by default: the app
    /// is served same-origin through the nginx/Vite proxy (ADR-11), so cross-origin access is opt-in
    /// per deployment. In Development every origin is reflected regardless of this list.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
