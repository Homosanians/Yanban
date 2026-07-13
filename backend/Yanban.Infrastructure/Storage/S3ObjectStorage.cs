using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;

namespace Yanban.Infrastructure.Storage;

/// <summary>
/// <see cref="IObjectStorage"/> over the AWS SDK's S3 client, pointed at any
/// S3-compatible endpoint (MinIO in dev) via <c>ServiceURL</c> + path-style addressing.
/// Presigned URLs are computed locally by the SDK, so no network call is made to issue one.
///
/// <para>Two clients, because the API and the browser reach storage at different hosts
/// (ADR-10). Everything that actually calls storage goes through <c>_s3</c>; the URLs handed
/// to the browser are signed by <c>_presign</c>, which names a host it need not be able to
/// reach itself — signing is local computation. When no public endpoint is configured the two
/// are the same instance.</para>
/// </summary>
public class S3ObjectStorage : IObjectStorage
{
    /// <summary>DI key for the presign-only client.</summary>
    public const string PresignClientKey = "presign";

    private readonly IAmazonS3 _s3;
    private readonly IAmazonS3 _presign;
    private readonly string _bucket;
    private readonly bool _presignEndpointIsHttp;

    public S3ObjectStorage(
        IAmazonS3 s3,
        [FromKeyedServices(PresignClientKey)] IAmazonS3 presign,
        IOptions<S3Options> options)
    {
        _s3 = s3;
        _presign = presign;
        _bucket = options.Value.Bucket;
        // AlignScheme rewrites URLs minted by the *presign* client, so the scheme it aligns to
        // must come from that client's endpoint. Keying it off the internal endpoint instead
        // works by accident whenever both are http (dev) and silently mangles the URL the moment
        // a deployment terminates TLS in front of storage but talks plaintext behind it.
        _presignEndpointIsHttp = Uri.TryCreate(options.Value.PresignEndpoint, UriKind.Absolute, out var uri)
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
        var url = _presign.GetPreSignedURL(new GetPreSignedUrlRequest
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
        var url = _presign.GetPreSignedURL(new GetPreSignedUrlRequest
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
    // is not part of the SigV2/SigV4 signature, so re-aligning it to the presign
    // endpoint's scheme (host, port and query untouched) keeps the signature valid.
    private string AlignScheme(string url) =>
        _presignEndpointIsHttp && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
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
