using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Moq;
using Xunit;

namespace NGB.Attachments.MinIO.IntegrationTests;

public sealed class MinioAdapterContractTests
{
    private static MinioAttachmentOptions Valid() => new()
    {
        InternalEndpoint = "https://internal.test", PublicEndpoint = "https://public.test", AccessKey = "app",
        SecretKey = "secret"
    };

    [Theory]
    [InlineData("InternalEndpoint", "bad")]
    [InlineData("InternalEndpoint", "http://storage.test")]
    [InlineData("PublicEndpoint", "ftp://storage.test")]
    [InlineData("PublicEndpoint", "https://storage.test/path")]
    [InlineData("PublicEndpoint", "https://user@storage.test")]
    [InlineData("PublicEndpoint", "https://storage.test/?x=1")]
    [InlineData("PublicEndpoint", "https://storage.test/#hash")]
    [InlineData("Bucket", "ab")]
    [InlineData("Bucket", "-bad")]
    [InlineData("Bucket", "bad-")]
    [InlineData("Bucket", "UPPER")]
    [InlineData("Bucket", "bad_bucket")]
    [InlineData("Bucket", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Bucket", null)]
    [InlineData("Region", "")]
    [InlineData("AccessKey", "")]
    [InlineData("SecretKey", "")]
    public void Invalid_configuration_is_rejected_on_resolution(string field, string? value)
    {
        var settings = Valid();
        typeof(MinioAttachmentOptions).GetProperty(field)!.SetValue(settings, value);
        Assert.False(settings.IsValid());
        var services = new ServiceCollection().AddNgbMinioAttachments(o =>
        {
            o.InternalEndpoint = settings.InternalEndpoint;
            o.PublicEndpoint = settings.PublicEndpoint;
            o.Bucket = settings.Bucket;
            o.Region = settings.Region;
            o.AccessKey = settings.AccessKey;
            o.SecretKey = settings.SecretKey;
        });
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IAttachmentObjectStorage>());
    }

    [Fact]
    public void Valid_TLS_and_explicit_local_HTTP_are_supported_with_bounded_timeout()
    {
        var settings = Valid();
        Assert.True(settings.IsValid());
        settings.Bucket = "bucket123";
        Assert.True(settings.IsValid());
        settings.InternalEndpoint = "http://localhost:9000";
        settings.AllowInsecureHttp = true;
        Assert.True(settings.IsValid());
        settings.RequestTimeoutSeconds = 0;
        Assert.False(settings.IsValid());
        settings.RequestTimeoutSeconds = 121;
        Assert.False(settings.IsValid());
        using var provider = new ServiceCollection().AddNgbMinioAttachments(o =>
        {
            o.InternalEndpoint = "https://internal.test";
            o.PublicEndpoint = "https://public.test";
            o.AccessKey = "app";
            o.SecretKey = "secret";
        }).BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IAttachmentObjectStorage>());
    }

    [Theory]
    [InlineData("sdk")]
    [InlineData("http")]
    [InlineData("io")]
    [InlineData("timeout")]
    public async Task Storage_failures_return_safe_errors_without_urls_credentials_or_inner_exception(string kind)
    {
        Exception failure = kind switch
        {
            "sdk" => new MinioException("signed-secret-url"),
            "http" => new HttpRequestException("signed-secret-url"), "io" => new IOException("signed-secret-url"),
            _ => new OperationCanceledException("signed-secret-url")
        };
        var client = new Mock<IMinioClient>();
        client.Setup(x => x.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        using var storage = new MinioAttachmentObjectStorage(Valid(), client.Object, client.Object);
        var error = await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(() =>
            storage.DeleteObjectAsync("attachments/test", default));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("signed-secret-url", error.ToString());
    }

    [Fact]
    public async Task Legacy_delete_contract_completes_when_storage_confirms_removal()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new Mock<IMinioClient>(MockBehavior.Strict);
        client.Setup(x => x.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), cancellation.Token))
            .Returns(Task.CompletedTask);
        client.Setup(x => x.Dispose());
        using var storage = new MinioAttachmentObjectStorage(Valid(), client.Object, client.Object);

        await storage.DeleteObjectAsync("legacy/object", cancellation.Token);

        client.Verify(x => x.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), cancellation.Token), Times.Once);
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_deletes_are_idempotent_and_cancellation_propagates_without_translation()
    {
        var client = new Mock<IMinioClient>();
        client.Setup(x => x.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ObjectNotFoundException("missing"));
        using var storage = new MinioAttachmentObjectStorage(Valid(), client.Object, client.Object);
        await storage.DeleteObjectAsync("missing", default);
        using var cts = new CancellationTokenSource();
        client.Setup(x => x.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), cts.Token)).Returns(() =>
        {
            cts.Cancel();
            return Task.FromCanceled(cts.Token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.DeleteObjectAsync("file", cts.Token));
        client.Setup(x => x.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), default))
            .ThrowsAsync(new InvalidOperationException("programmer error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteObjectAsync("file", default));
    }
}
