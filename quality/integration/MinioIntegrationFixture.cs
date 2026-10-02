using NGB.Testing.Containers;
using Testcontainers.Minio;

namespace NGB.Testing.Minio;

/// <summary>
/// Owns isolated object storage and provisions the same bucket policy as local Compose.
/// </summary>
public sealed class MinioIntegrationFixture : IAsyncDisposable
{
    private const string DefaultImage =
        "ghcr.io/coollabsio/minio:RELEASE.2025-10-15T17-29-55Z" +
        "@sha256:69b55a1c1c5dc285ce04db96689f5b2102317fc77a50680a1874ca6efd1c87f9";
    private readonly MinioContainer _container;

    public MinioIntegrationFixture(string? browserOrigin = null)
    {
        var image = Environment.GetEnvironmentVariable("NGB_TEST_MINIO_IMAGE");
        if (string.IsNullOrWhiteSpace(image))
            image = DefaultImage;

        _container = new MinioBuilder(image)
            .WithUsername("ngb-test-root")
            .WithPassword(Guid.NewGuid().ToString("N"))
            .WithEnvironment("MINIO_ENDPOINT", "http://localhost:9000")
            .WithEnvironment("MINIO_BUCKET", Bucket)
            .WithEnvironment("MINIO_APP_ACCESS_KEY", AccessKey)
            .WithEnvironment("MINIO_APP_SECRET_KEY", SecretKey)
            .WithEnvironment("MINIO_API_CORS_ALLOW_ORIGIN", browserOrigin ?? "http://localhost")
            .WithResourceMapping(
                new FileInfo(Path.Combine(AppContext.BaseDirectory, "Infrastructure", "minio-init.sh")),
                new FileInfo("/tmp/minio-init.sh"))
            .Build();
    }

    public string Bucket { get; } = "attachments-tests";

    public string AccessKey { get; } = $"ngb-app-{Guid.NewGuid():N}";

    public string SecretKey { get; } = Guid.NewGuid().ToString("N");

    public string Endpoint => _container.GetConnectionString().TrimEnd('/');

    public async Task InitializeAsync()
    {
        try
        {
            await using (var startupLease = await TestcontainerStartupGate.AcquireAsync())
                await _container.StartAsync();

            await ProvisionAsync();
        }
        catch
        {
            await _container.DisposeAsync();
            throw;
        }
    }

    public async Task ProvisionAsync()
    {
        var result = await _container.ExecAsync(["sh", "/tmp/minio-init.sh"]);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"MinIO test bootstrap failed: {result.Stderr}");
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
