using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Attachments;
using NGB.Notes;
using NGB.Runtime.Attachments;
using NGB.Runtime.BusinessObjects;
using NGB.Runtime.Notes;

namespace NGB.Runtime.DependencyInjection;

public static class AttachmentsNotesServiceCollectionExtensions
{
    /// <summary>Registers provider-neutral services and validates resource limits at startup.</summary>
    public static IServiceCollection AddNgbAttachmentsAndNotes(this IServiceCollection services,
        Action<AttachmentOptions>? attachments = null,
        Action<NoteOptions>? notes = null)
    {
        var attachmentOptions = services.AddOptions<AttachmentOptions>();
        if (attachments is not null)
            attachmentOptions.Configure(attachments);

        attachmentOptions.Validate(o => o.IsValid(), "Invalid attachment limits or lifetimes.").ValidateOnStart();

        var noteOptions = services.AddOptions<NoteOptions>();
        if (notes is not null)
            noteOptions.Configure(notes);

        noteOptions.Validate(o => o.IsValid(), "Invalid note text limit.").ValidateOnStart();

        services.TryAddScoped<IBusinessObjectResolver, BusinessObjectResolver>();
        services.TryAddScoped<BusinessObjectContentAccess>();
        services.TryAddScoped<IBusinessObjectContentSummaryService, BusinessObjectContentSummaryService>();
        services.TryAddScoped<IAttachmentService, AttachmentService>();
        services.TryAddScoped<INoteService, NoteService>();
        services.TryAddScoped<AttachmentCleanupQueue>();
        services.TryAddScoped<IAttachmentMaintenance, AttachmentMaintenance>();

        return services;
    }
}
