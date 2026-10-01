using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Attachments;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Notes;
using NGB.Runtime.DependencyInjection;

namespace NGB.Api.Attachments;

public static class AttachmentsNotesApiExtensions
{
    /// <summary>Registers API hosting, validated limits and outbox-driven attachment maintenance.</summary>
    public static IServiceCollection AddNgbAttachmentsNotesApi(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddNgbAttachmentsAndNotes();
        services.Configure<AttachmentOptions>(configuration.GetSection("Attachments"));
        services.Configure<NoteOptions>(configuration.GetSection("Notes"));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INgbExceptionHttpMapper, AttachmentStorageExceptionHttpMapper>());
        services.AddHostedService<AttachmentMaintenanceHostedService>();
        
        return services;
    }
}
