using System.Security.Cryptography;
using Testcontainers.Minio;

namespace EquipmentInspection.IntegrationTests;

internal sealed class TestAttachmentStorage : IAsyncDisposable
{
    private const string Image = "ghcr.io/coollabsio/minio:RELEASE.2025-10-15T17-29-55Z"
        + "@sha256:69b55a1c1c5dc285ce04db96689f5b2102317fc77a50680a1874ca6efd1c87f9";
    private const string Bucket = "equipment-attachments";
    private readonly string _accessKey = "fixture-" + Guid.NewGuid().ToString("N");
    private readonly string _secretKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly MinioContainer _container;

    public TestAttachmentStorage(string? browserOrigin = null)
    {
        _container = new MinioBuilder(Image)
            .WithUsername("fixture-root")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
            .WithEnvironment("NGB_APP_ACCESS_KEY", _accessKey)
            .WithEnvironment("NGB_APP_SECRET_KEY", _secretKey)
            .WithEnvironment("MINIO_API_CORS_ALLOW_ORIGIN", browserOrigin ?? "http://localhost")
            .Build();
    }

    public IReadOnlyDictionary<string, string> Configuration => new Dictionary<string, string>
    {
        ["FeatureManagement:Attachments"] = "true",
        ["Attachments:MinIO:InternalEndpoint"] = _container.GetConnectionString(),
        ["Attachments:MinIO:PublicEndpoint"] = _container.GetConnectionString(),
        ["Attachments:MinIO:Bucket"] = Bucket,
        ["Attachments:MinIO:AccessKey"] = _accessKey,
        ["Attachments:MinIO:SecretKey"] = _secretKey,
        ["Attachments:MinIO:AllowInsecureHttp"] = "true",
        ["Attachments:MinIO:RequestTimeoutSeconds"] = "2"
    };

    public async Task StartAsync()
    {
        await _container.StartAsync();
        const string provision = """
            mc alias set fixture http://localhost:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
            mc mb --ignore-existing fixture/equipment-attachments >/dev/null
            mc anonymous set none fixture/equipment-attachments >/dev/null
            cat > /tmp/attachments-policy.json <<'POLICY'
            {
              "Version": "2012-10-17",
              "Statement": [
                {
                  "Effect": "Allow",
                  "Action": ["s3:GetBucketLocation", "s3:ListBucket"],
                  "Resource": ["arn:aws:s3:::equipment-attachments"]
                },
                {
                  "Effect": "Allow",
                  "Action": ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"],
                  "Resource": ["arn:aws:s3:::equipment-attachments/uploads/*", "arn:aws:s3:::equipment-attachments/attachments/*"]
                }
              ]
            }
            POLICY
            mc admin user add fixture "$NGB_APP_ACCESS_KEY" "$NGB_APP_SECRET_KEY" >/dev/null
            mc admin policy create fixture attachments /tmp/attachments-policy.json >/dev/null
            mc admin policy attach fixture attachments --user "$NGB_APP_ACCESS_KEY" >/dev/null
            """;
        var result = await _container.ExecAsync(["sh", "-eu", "-c", provision]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Disposable attachment storage provisioning failed.");
    }

    public async Task SetApplicationAccessAsync(bool enabled)
    {
        var command = enabled
            ? "mc admin user enable fixture \"$NGB_APP_ACCESS_KEY\" >/dev/null"
            : "mc admin user disable fixture \"$NGB_APP_ACCESS_KEY\" >/dev/null";
        var result = await _container.ExecAsync(["sh", "-eu", "-c", command]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Could not change disposable storage access.");
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
