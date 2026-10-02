using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Application.Abstractions.Features;
using NGB.Attachments;
using NGB.Notes;
using NGB.Runtime.Attachments;
using NGB.Runtime.BusinessObjects;
using NGB.Runtime.Features;
using NGB.Runtime.Notes;

namespace NGB.Runtime.DependencyInjection;

public static class AttachmentsNotesServiceCollectionExtensions
{
    /// <summary>Registers feature-gated provider-neutral services. Register feature management first to enable them.</summary>
    public static IServiceCollection AddNgbAttachmentsAndNotes(this IServiceCollection services,
        Action<AttachmentOptions>? attachments = null,
        Action<NoteOptions>? notes = null)
    {
        if (services.All(x => x.ServiceType != typeof(NgbFeatureRegistry)))
            services.AddNgbFeatureManagement(new ConfigurationBuilder().Build());

        var attachmentOptions = services.AddOptions<AttachmentOptions>();
        if (attachments is not null)
            attachmentOptions.Configure(attachments);

        attachmentOptions.Validate(o => o.IsValid(), "Invalid attachment limits or lifetimes.");

        var noteOptions = services.AddOptions<NoteOptions>();
        if (notes is not null)
            noteOptions.Configure(notes);

        noteOptions.Validate(o => o.IsValid(), "Invalid note text limit.");

        services.TryAddScoped<IBusinessObjectResolver, BusinessObjectResolver>();
        services.TryAddScoped<BusinessObjectContentAccess>();
        services.TryAddScoped<IBusinessObjectContentSummaryService>(sp => new FeatureContentSummaryService(
            sp.GetRequiredService<INgbFeatureService>(),
            () => ActivatorUtilities.CreateInstance<BusinessObjectContentSummaryService>(sp)));
        services.TryAddScoped<IAttachmentService>(sp => new FeatureAttachmentService(
            sp.GetRequiredService<INgbFeatureService>(),
            () => ActivatorUtilities.CreateInstance<AttachmentService>(sp)));
        services.TryAddScoped<INoteService>(sp => new FeatureNoteService(
            sp.GetRequiredService<INgbFeatureService>(),
            () => ActivatorUtilities.CreateInstance<NoteService>(sp)));
        services.TryAddScoped<AttachmentCleanupQueue>();
        services.TryAddScoped<IAttachmentMaintenance>(sp => ActivatorUtilities.CreateInstance<AttachmentMaintenance>(sp));

        return services;
    }
}
