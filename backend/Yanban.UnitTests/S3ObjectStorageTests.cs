using Microsoft.Extensions.Options;
using Shouldly;
using Yanban.Application.Common;
using Yanban.Infrastructure.Storage;

namespace Yanban.UnitTests;

/// <summary>
/// The API reaches storage at one host and the browser at another, so a presigned URL must
/// be signed for the host the browser uses. Presigning is a local signature computation with
/// no network call, which is why a client can sign for an endpoint it could never reach itself.
/// </summary>
public class S3ObjectStorageTests
{
    private const string Internal = "http://minio:9000";
    private const string Public = "http://localhost:9000";

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(15);

    /// <summary>Wires the two clients the same way <c>DependencyInjection</c> does.</summary>
    private static S3ObjectStorage Create(string endpoint, string publicEndpoint)
    {
        var options = new S3Options
        {
            Endpoint = endpoint,
            PublicEndpoint = publicEndpoint,
            AccessKey = "minioadmin",
            SecretKey = "minioadmin",
            Bucket = "yanban-attachments"
        };

        // Go through the same factory the API uses, so the signing config under test is
        // the one that actually ships, not a copy that could drift.
        var control = S3ClientFactory.Create(options.Endpoint, options);
        var presign = string.IsNullOrWhiteSpace(options.PublicEndpoint)
            ? control
            : S3ClientFactory.Create(options.PublicEndpoint, options);

        return new S3ObjectStorage(control, presign, Options.Create(options));
    }

    [Fact]
    public void UploadUrl_IsSignedForThePublicHost_NotTheApisInternalOne()
    {
        var storage = Create(Internal, Public);

        var (url, _) = storage.CreateUploadUrl("cards/a/file.png", "image/png", Expiry);

        var uri = new Uri(url);
        uri.Host.ShouldBe("localhost", "a browser cannot resolve the host the API reaches storage on");
        uri.Port.ShouldBe(9000);
        // Still signed. The host is part of the signature, so this cannot be a post-hoc string
        // rewrite of the internal URL; it has to have been signed for the public host.
        uri.Query.ShouldContain("X-Amz-Signature");
    }

    [Fact]
    public void DownloadUrl_IsSignedForThePublicHost()
    {
        var storage = Create(Internal, Public);

        var (url, _) = storage.CreateDownloadUrl("cards/a/file.png", "file.png", "image/png", Expiry);

        new Uri(url).Host.ShouldBe("localhost");
    }

    [Fact]
    public void WithoutAPublicEndpoint_PresigningFallsBackToTheInternalEndpoint()
    {
        // The "run the API on the host" case: both sides reach storage identically, so the
        // fix must be a no-op rather than a second behaviour to reason about.
        var storage = Create(Internal, publicEndpoint: "");

        var (url, _) = storage.CreateUploadUrl("cards/a/file.png", "image/png", Expiry);

        new Uri(url).Host.ShouldBe("minio");
    }

    [Fact]
    public void TheUrlScheme_FollowsThePresignEndpoint_NotTheInternalOne()
    {
        // The SDK emits https regardless, and AlignScheme corrects it. After the client split
        // the scheme it corrects to must come from the presign endpoint: keying it off the
        // internal one leaves this URL https, and the browser would then try TLS against a
        // plaintext MinIO. Both endpoints are http in dev, so only a split like this catches it.
        var storage = Create("https://minio:9000", "http://localhost:9000");

        var (url, _) = storage.CreateUploadUrl("cards/a/file.png", "image/png", Expiry);

        new Uri(url).Scheme.ShouldBe("http");
    }
}
