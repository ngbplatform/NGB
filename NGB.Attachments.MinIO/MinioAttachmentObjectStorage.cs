using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace NGB.Attachments.MinIO;

internal sealed class MinioAttachmentObjectStorage : IAttachmentObjectStorage, IDisposable
{
    private readonly IMinioClient _service;
    private readonly IMinioClient _signer;
    private readonly MinioAttachmentOptions _options;

    public MinioAttachmentObjectStorage(IOptions<MinioAttachmentOptions> settings)
    {
        _options = settings.Value;
        _service = CreateClient(_options.InternalEndpoint, _options);
        _signer = CreateClient(_options.PublicEndpoint, _options);
    }

    internal MinioAttachmentObjectStorage(MinioAttachmentOptions options, IMinioClient service, IMinioClient signer)
    {
        _options = options;
        _service = service;
        _signer = signer;
    }

    private static IMinioClient CreateClient(string endpoint, MinioAttachmentOptions options)
    {
        var uri = new Uri(endpoint);

        return new MinioClient()
            .WithEndpoint(uri.Host, uri.Port)
            .WithSSL(uri.Scheme == "https")
            .WithCredentials(options.AccessKey, options.SecretKey)
            .WithRegion(options.Region)
            .WithTimeout(options.RequestTimeoutSeconds * 1000)
            .Build();
    }

    public Task<AttachmentUploadTarget> CreateUploadTargetAsync(
        string key,
        string contentType,
        TimeSpan lifetime,
        CancellationToken ct)
        => SafeAsync(async () => new AttachmentUploadTarget(await _signer.PresignedPutObjectAsync(
                new PresignedPutObjectArgs()
                    .WithBucket(_options.Bucket)
                    .WithObject(key)
                    .WithExpiry((int)lifetime.TotalSeconds))
                .WaitAsync(ct),
            new Dictionary<string, string> { ["Content-Type"] = contentType }),
            ct);

    public Task<AttachmentStoredObject?> GetObjectInfoAsync(string key, CancellationToken ct)
        => SafeAsync<AttachmentStoredObject?>(async () =>
        {
            try
            {
                var info = await _service.StatObjectAsync(
                    new StatObjectArgs()
                        .WithBucket(_options.Bucket)
                        .WithObject(key),
                    ct);

                return new(info.Size, info.ContentType, info.ETag);
            }
            catch (ObjectNotFoundException)
            {
                return null;
            }
        }, ct);

    public Task SealUploadAsync(string uploadKey, string destinationKey, string expectedETag, CancellationToken ct)
        => SafeAsync(async () =>
        {
            var conditions = new CopyConditions();
            conditions.SetMatchETag(expectedETag);
            var source = new CopySourceObjectArgs()
                .WithBucket(_options.Bucket).WithObject(uploadKey)
                .WithCopyConditions(conditions);

            await _service.CopyObjectAsync(
                new CopyObjectArgs()
                    .WithBucket(_options.Bucket)
                    .WithObject(destinationKey)
                    .WithCopyObjectSource(source),
                ct);

            return true;
        }, ct);

    public Task<string> CreateDownloadTargetAsync(string key, string fileName, TimeSpan lifetime, CancellationToken ct)
        => SafeAsync(async () =>
        {
            var disposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName };
            
            return await _signer.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(key)
                .WithExpiry((int)lifetime.TotalSeconds)
                .WithHeaders(new Dictionary<string, string>
                {
                    ["response-content-disposition"] = disposition.ToString(),
                    ["response-content-type"] = "application/octet-stream",
                    ["response-cache-control"] = "no-store"
                }))
                .WaitAsync(ct);
        }, ct);

    public Task DeleteObjectAsync(string key, CancellationToken ct)
        => SafeAsync(async () =>
        {
            try
            {
                await _service.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(_options.Bucket).WithObject(key), ct);
            }
            catch (ObjectNotFoundException)
            {
            }

            return true;
        }, ct);

    private static async Task<T> SafeAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is MinioException or HttpRequestException or OperationCanceledException or IOException)
        {
            // Deliberately drop SDK exception details: requests may include signed query parameters.
            throw new AttachmentStorageUnavailableException();
        }
    }

    public void Dispose()
    {
        _service.Dispose();
        _signer.Dispose();
    }
}
