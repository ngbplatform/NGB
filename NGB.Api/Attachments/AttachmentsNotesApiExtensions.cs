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
    /// <summary>Registers API hosting, validated limits and outbox-driven attachment maintenance.</summary>
    public static IServiceCollection AddNgbAttachmentsNotesApi(this IServiceCollection services,
        IConfiguration configuration)
        => services.AddNgbAttachmentsNotesApi(configuration, null);

    /// <summary>Configures storage only for enabled attachments or explicitly retained maintenance.</summary>
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
        var maintenanceEnabled = configuration.GetValue<bool>("Attachments:MaintenanceEnabled");

        if (attachmentsEnabled || maintenanceEnabled)
        {
            configureStorage?.Invoke(services);
            services.AddOptions<AttachmentOptions>().ValidateOnStart();
            services.AddHostedService<AttachmentStorageStartupValidator>();
            services.AddHostedService<AttachmentMaintenanceHostedService>();
        }

        if (notesEnabled)
            services.AddOptions<NoteOptions>().ValidateOnStart();

        return services;
    }
}
