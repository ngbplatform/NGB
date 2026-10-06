using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Attachments;
using NGB.Core.Features;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Notes;
using NGB.Runtime.DependencyInjection;

namespace NGB.Api.Attachments;

public static class AttachmentsNotesApiExtensions
{
    /// <summary>Registers API hosting, validated limits and pending upload expiration.</summary>
    public static IServiceCollection AddNgbAttachmentsNotesApi(this IServiceCollection services,
        IConfiguration configuration)
        => services.AddNgbAttachmentsNotesApi(configuration, null);

    /// <summary>Configures storage only for enabled attachments. Pending upload expiration uses only the database.</summary>
    public static IServiceCollection AddNgbAttachmentsNotesApi(this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceCollection>? configureStorage)
    {
        services.AddNgbFeatureManagement(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddNgbAttachmentsAndNotes();
        services.Configure<AttachmentOptions>(configuration.GetSection("Attachments"));
        services.Configure<NoteOptions>(configuration.GetSection("Notes"));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INgbExceptionHttpMapper, AttachmentStorageExceptionHttpMapper>());

        var attachmentsEnabled = configuration.GetValue<bool>($"FeatureManagement:{NgbFeatures.Attachments}");
        var notesEnabled = configuration.GetValue<bool>($"FeatureManagement:{NgbFeatures.Notes}");
        var uploadExpirationEnabled = configuration.GetValue<bool>("Attachments:UploadExpirationEnabled");

        if (attachmentsEnabled)
        {
            configureStorage?.Invoke(services);
            services.AddHostedService<AttachmentStorageStartupValidator>();
        }

        if (attachmentsEnabled || uploadExpirationEnabled)
        {
            services.AddOptions<AttachmentOptions>().ValidateOnStart();
            services.AddHostedService<AttachmentUploadExpirationHostedService>();
        }

        if (notesEnabled)
            services.AddOptions<NoteOptions>().ValidateOnStart();

        return services;
    }
}
