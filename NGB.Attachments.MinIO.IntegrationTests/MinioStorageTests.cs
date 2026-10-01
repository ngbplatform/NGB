using System.Net;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;
using NGB.Testing.Containers;
using Xunit;

namespace NGB.Attachments.MinIO.IntegrationTests;

public sealed class MinioStorageTests : IAsyncLifetime
{
    private IContainer container = null!;
    private IFutureDockerImage image = null!;
    private ServiceProvider provider = null!;
    private IAttachmentObjectStorage storage = null!;
    private string endpoint = "";
    private const string Bucket = "attachments-tests";

    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "NGB.sln"))) root = root.Parent;
        image = new ImageFromDockerfileBuilder().WithDockerfileDirectory(Path.Combine(root!.FullName, "docker/minio"))
            .WithName("ngb-minio-dev:2025-10-15").WithDeleteIfExists(false).WithCleanUp(false).Build();
        await image.CreateAsync();
        container = new ContainerBuilder("ngb-minio-dev:2025-10-15")
            .WithResourceMapping(Path.Combine(root!.FullName, "docker/minio/init.sh"), "/tmp/")
            .WithEnvironment("MINIO_ROOT_USER", "integration-user")
            .WithEnvironment("MINIO_ROOT_PASSWORD", "integration-secret-only")
            .WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPort(9000).ForPath("/minio/health/live")))
            .Build();
        await using (await TestcontainerStartupGate.AcquireAsync()) await container.StartAsync();
        var port = container.GetMappedPublicPort(9000);
        endpoint = $"http://localhost:{port}";
        using var admin = new MinioClient().WithEndpoint("localhost", port)
            .WithCredentials("integration-user", "integration-secret-only").Build();
        await admin.MakeBucketAsync(new MakeBucketArgs().WithBucket(Bucket));
        // The same script provisions local compose stacks. Re-running initialization must be safe.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var initialized = await container.ExecAsync([
                "env", "MINIO_ENDPOINT=http://localhost:9000", "MINIO_ROOT_USER=integration-user",
                "MINIO_ROOT_PASSWORD=integration-secret-only", $"MINIO_BUCKET={Bucket}",
                "MINIO_APP_ACCESS_KEY=attachment-app",
                "MINIO_APP_SECRET_KEY=application-test-secret", "sh", "/tmp/init.sh"
            ]);
            Assert.True(initialized.ExitCode == 0, initialized.Stderr);
        }

        var services = new ServiceCollection();
        services.AddNgbMinioAttachments(options =>
        {
            options.InternalEndpoint = $"http://127.0.0.1:{port}";
            options.PublicEndpoint = endpoint;
            options.AccessKey = "attachment-app";
            options.SecretKey = "application-test-secret";
            options.Bucket = Bucket;
            options.AllowInsecureHttp = true;
        });
        provider = services.BuildServiceProvider();
        storage = provider.GetRequiredService<IAttachmentObjectStorage>();
    }

    [Fact]
    public async Task Presigned_upload_stat_conditional_seal_private_download_and_idempotent_delete()
    {
        using var http = new HttpClient();
        Assert.Null(await storage.GetObjectInfoAsync("uploads/missing", default));
        var upload =
            await storage.CreateUploadTargetAsync("uploads/test", "text/plain", TimeSpan.FromMinutes(10), default);
        Assert.StartsWith(endpoint, upload.Url);
        using var put = new HttpRequestMessage(HttpMethod.Put, upload.Url)
            { Content = new StringContent("abc", Encoding.UTF8, "text/plain") };
        put.Content.Headers.ContentType = new("text/plain");
        using var uploaded = await http.SendAsync(put);
        uploaded.EnsureSuccessStatusCode();
        var info = await storage.GetObjectInfoAsync("uploads/test", default);
        Assert.NotNull(info);
        Assert.Equal(3, info.SizeBytes);
        Assert.Equal("text/plain", info.ContentType);
        await storage.SealUploadAsync("uploads/test", "attachments/test", info.ETag, default);
        // A still valid signed PUT must not mutate the sealed resource.
        using var replay = await http.PutAsync(upload.Url, new StringContent("replaced"));
        replay.EnsureSuccessStatusCode();
        var download =
            await storage.CreateDownloadTargetAsync("attachments/test", "résumé.txt", TimeSpan.FromMinutes(3), default);
        using var downloaded = await http.GetAsync(download);
        downloaded.EnsureSuccessStatusCode();
        Assert.Equal("abc", await downloaded.Content.ReadAsStringAsync());
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("résumé.txt", downloaded.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("application/octet-stream", downloaded.Content.Headers.ContentType?.MediaType);
        using var anonymous = await http.GetAsync($"{endpoint}/{Bucket}/attachments/test");
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
        await storage.DeleteObjectAsync("attachments/test", default);
        await storage.DeleteObjectAsync("attachments/test", default);
        Assert.Null(await storage.GetObjectInfoAsync("attachments/test", default));
        await storage.DeleteObjectAsync("uploads/test", default);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.GetObjectInfoAsync("anything", cts.Token));
    }

    public async Task DisposeAsync()
    {
        if (provider is not null)
            await provider.DisposeAsync();
        if (container is not null)
            await container.DisposeAsync();
        if (image is not null)
            await image.DisposeAsync();
    }
}