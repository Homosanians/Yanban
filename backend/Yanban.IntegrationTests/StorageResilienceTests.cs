using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Yanban.Application.Common;
using Yanban.Infrastructure.Storage;

namespace Yanban.IntegrationTests;

/// <summary>
/// The startup bucket-ensure is deliberately tolerant of storage being down so the API
/// still boots (README / ADR-10) — a dev running without MinIO must still get a working
/// auth loop. The boot path swallows exactly <c>HttpRequestException or AmazonClientException</c>.
/// This pins that the realistic down-path (nothing listening) fails in a way that catch
/// tolerates — verified rather than assumed — without needing a container.
/// </summary>
public class StorageResilienceTests
{
    [Fact]
    public async Task EnsureBucket_AgainstUnreachableStorage_FailsInAWayStartupTolerates()
    {
        const string deadEndpoint = "http://127.0.0.1:1"; // nothing listens here
        var config = new AmazonS3Config
        {
            ServiceURL = deadEndpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            MaxErrorRetry = 0,
            Timeout = TimeSpan.FromSeconds(5)
        };
        var s3 = new AmazonS3Client(new BasicAWSCredentials("key", "secret"), config);
        var storage = new S3ObjectStorage(s3, Options.Create(new S3Options { Endpoint = deadEndpoint, Bucket = "b" }));

        // Mirrors the startup catch filter in Program.cs. Connection-refused surfaces here
        // as a raw HttpRequestException; the AmazonClientException arm covers the
        // service-reachable-but-erroring case.
        var ex = await Should.ThrowAsync<Exception>(() => storage.EnsureBucketAsync(CancellationToken.None));
        (ex is HttpRequestException or AmazonClientException).ShouldBeTrue($"unexpected type: {ex.GetType()}");
    }
}
