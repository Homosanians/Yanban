using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Yanban.Application.Common;

namespace Yanban.Infrastructure.Storage;

/// <summary>
/// Builds the S3 SDK clients. The one place their signing configuration lives, so the
/// tests exercise the same construction the API uses rather than a copy of it.
/// </summary>
public static class S3ClientFactory
{
    static S3ClientFactory()
    {
        // Presign with SigV4, not the SDK's legacy SigV2 (its default when ServiceURL points at a
        // custom endpoint in a v2-capable region). Two reasons, one practical and one structural:
        //
        //   - AWS stopped accepting SigV2 for buckets created after June 2020, so SigV2 URLs work
        //     against MinIO and then fail the day this is pointed at real S3.
        //   - SigV4 signs the Host header. SigV2 does not — under it a presigned URL's host could
        //     be string-replaced after the fact, which is exactly the shortcut the split-endpoint
        //     design below exists to avoid taking.
        //
        // The SDK only reads this global; AmazonS3Config.SignatureVersion is ignored for presigning
        // (verified: setting it alone still emitted "AWSAccessKeyId=...&Signature=...").
        AWSConfigsS3.UseSignatureVersion4 = true;
    }

    public static AmazonS3Client Create(string endpoint, S3Options options) =>
        new(new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint,
                // MinIO serves buckets as path segments, not DNS subdomains.
                ForcePathStyle = true,
                // SigV4 signatures are region-scoped; MinIO accepts any region but one must be named.
                AuthenticationRegion = "us-east-1"
            });
}
