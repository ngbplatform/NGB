using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NGB.Testing.Minio;
using Xunit;

namespace NGB.Attachments.MinIO.IntegrationTests;

public sealed class MinioStorageTests : IAsyncLifetime
{
    private readonly MinioIntegrationFixture _fixture = new();
    private ServiceProvider? _provider;
    private IAttachmentObjectStorage _storage = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();

        var services = new ServiceCollection();
        services.AddNgbMinioAttachments(options =>
        {
            options.InternalEndpoint = _fixture.Endpoint;
            options.PublicEndpoint = _fixture.Endpoint;
            options.AccessKey = _fixture.AccessKey;
            options.SecretKey = _fixture.SecretKey;
            options.Bucket = _fixture.Bucket;
            options.AllowInsecureHttp = true;
        });

        _provider = services.BuildServiceProvider();
        _storage = _provider.GetRequiredService<IAttachmentObjectStorage>();
    }

    [Fact]
    public async Task Presigned_upload_seals_private_bytes_and_application_cannot_delete_them()
    {
        using var http = new HttpClient();
        Assert.Null(await _storage.GetObjectInfoAsync("uploads/missing", default));

        var upload = await _storage.CreateUploadTargetAsync(
            "uploads/test", "text/plain", TimeSpan.FromMinutes(10), default);
        Assert.StartsWith(_fixture.Endpoint, upload.Url);

        using var put = new HttpRequestMessage(HttpMethod.Put, upload.Url)
        {
            Content = new StringContent("abc", Encoding.UTF8, "text/plain")
        };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var uploaded = await http.SendAsync(put);
        uploaded.EnsureSuccessStatusCode();

        var info = await _storage.GetObjectInfoAsync("uploads/test", default);
        Assert.NotNull(info);
        Assert.Equal(3, info.SizeBytes);
        Assert.Equal("text/plain", info.ContentType);

        await _storage.SealUploadAsync("uploads/test", "attachments/test", info.ETag, default);

        // A still-valid signed PUT must not mutate the sealed resource.
        using var replay = await http.PutAsync(upload.Url, new StringContent("replaced"));
        replay.EnsureSuccessStatusCode();

        // Bootstrap removes the old expiry rule while retaining both stored objects.
        await _fixture.AddLegacyStagingExpiryAsync();
        Assert.True(await _fixture.HasLegacyStagingExpiryAsync());
        await _fixture.ProvisionAsync();
        Assert.False(await _fixture.HasLegacyStagingExpiryAsync());

        var download = await _storage.CreateDownloadTargetAsync(
            "attachments/test", "résumé.txt", TimeSpan.FromMinutes(3), default);
        using var downloaded = await http.GetAsync(download);
        downloaded.EnsureSuccessStatusCode();
        Assert.Equal("abc", await downloaded.Content.ReadAsStringAsync());
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("résumé.txt", downloaded.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("application/octet-stream", downloaded.Content.Headers.ContentType?.MediaType);

        using var anonymous = await http.GetAsync($"{_fixture.Endpoint}/{_fixture.Bucket}/attachments/test");
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);

        await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(() =>
            _storage.DeleteObjectAsync("attachments/test", default));
        await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(() =>
            _storage.DeleteObjectAsync("uploads/test", default));
        Assert.NotNull(await _storage.GetObjectInfoAsync("attachments/test", default));
        Assert.NotNull(await _storage.GetObjectInfoAsync("uploads/test", default));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _storage.GetObjectInfoAsync("anything", cancellation.Token));
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _fixture.DisposeAsync();
    }
}
