using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;

namespace Yanban.Infrastructure.Storage;

/// <summary>
/// <see cref="IObjectStorage"/> over the AWS SDK's S3 client, pointed at any
/// S3-compatible endpoint (MinIO in dev) via <c>ServiceURL</c> + path-style addressing.
/// Presigned URLs are computed locally by the SDK, so no network call is made to issue one.
/// </summary>
public class S3ObjectStorage : IObjectStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly bool _endpointIsHttp;

    public S3ObjectStorage(IAmazonS3 s3, IOptions<S3Options> options)
    {
        _s3 = s3;
        _bucket = options.Value.Bucket;
        _endpointIsHttp = Uri.TryCreate(options.Value.Endpoint, UriKind.Absolute, out var uri)
                          && uri.Scheme == Uri.UriSchemeHttp;
    }

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (!await AmazonS3Util.DoesS3BucketExistV2Async(_s3, _bucket))
            await _s3.PutBucketAsync(_bucket, ct);
    }

    public (string Url, DateTimeOffset ExpiresAt) CreateUploadUrl(string key, string contentType, TimeSpan expiry)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        // Pinning ContentType makes it a signed header: the client's PUT must send exactly
        // this Content-Type or storage rejects it with SignatureDoesNotMatch.
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            ContentType = contentType
        });
        return (AlignScheme(url), expiresAt);
    }

    public (string Url, DateTimeOffset ExpiresAt) CreateDownloadUrl(string key, string fileName, string contentType, TimeSpan expiry)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        // Override the response headers so the browser downloads under the original file
        // name and type regardless of how the object is stored.
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = $"attachment; filename=\"{fileName}\"",
                ContentType = contentType
            }
        });
        return (AlignScheme(url), expiresAt);
    }

    // The SDK emits an https presigned URL even when the endpoint is plain http (MinIO
    // in dev); UseHttp is documented not to apply once ServiceURL is set. The URL scheme
    // is not part of the SigV2/SigV4 signature, so re-aligning it to the configured
    // endpoint's scheme (host, port and query untouched) keeps the signature valid.
    private string AlignScheme(string url) =>
        _endpointIsHttp && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? string.Concat("http://", url.AsSpan("https://".Length))
            : url;

    public async Task<long?> TryGetObjectSizeAsync(string key, CancellationToken ct)
    {
        try
        {
            var meta = await _s3.GetObjectMetadataAsync(_bucket, key, ct);
            return meta.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct) =>
        _s3.DeleteObjectAsync(_bucket, key, ct);
}
