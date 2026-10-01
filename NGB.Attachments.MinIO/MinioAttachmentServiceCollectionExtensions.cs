using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NGB.Attachments.MinIO;

public static class MinioAttachmentServiceCollectionExtensions
{
    /// <summary>Explicit host selection of the MinIO adapter. Credentials are supplied by host configuration.</summary>
    public static IServiceCollection AddNgbMinioAttachments(this IServiceCollection services,
        Action<MinioAttachmentOptions> configure)
    {
        services.AddOptions<MinioAttachmentOptions>().Configure(configure)
            .Validate(o => o.IsValid(), "Invalid MinIO attachment configuration; endpoints, credentials, private bucket and bounded timeout are required.")
            .ValidateOnStart();

        services.TryAddSingleton<IAttachmentObjectStorage, MinioAttachmentObjectStorage>();

        return services;
    }
}
