using System.Text.Json;
using System.Text.Json.Serialization;
using NGB.Application.Abstractions.Features;
using NGB.Contracts.Audit;
using NGB.Contracts.BusinessObjects;
using NGB.Core.Features;
using NGB.Core.Security;
using NGB.Runtime.Security;

namespace NGB.Runtime.BusinessObjects;

internal sealed class ContentAuditAccess(
    INgbFeatureService features,
    INgbAccessChecker access,
    BusinessObjectContentAccess contentAccess)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task RequireAttachmentAsync(BusinessObjectRef target, CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Attachments, ct);
        await contentAccess.RequireAsync(target, NgbSystemPermissions.AttachmentsRead, ct);

        if (!await access.HasAsync(NgbResourceKinds.System, NgbPermissionResources.Audit, NgbPermissionActions.View, ct))
        {
            await access.RequireAsync(
                target.Kind == BusinessObjectKind.CatalogItem
                    ? NgbResourceKinds.Catalog
                    : NgbResourceKinds.Document,
                target.TypeCode,
                NgbPermissionActions.ViewAudit,
                ct);
        }
    }

    public async Task<AuditLogPageDto> FilterAsync(AuditLogPageDto page, CancellationToken ct)
    {
        var items = new List<AuditEventDto>(page.Items.Count);
        var allowedTargets = new HashSet<(BusinessObjectRef Target, bool Attachment)>();

        foreach (var item in page.Items)
        {
            var attachment = item.ActionCode.StartsWith("attachments.", StringComparison.Ordinal);
            var note = item.ActionCode.StartsWith("notes.", StringComparison.Ordinal);
            if (!attachment && !note)
            {
                items.Add(item);
                continue;
            }

            var feature = attachment ? NgbFeatures.Attachments : NgbFeatures.Notes;
            var permission = attachment ? NgbSystemPermissions.AttachmentsRead : NgbSystemPermissions.NotesRead;
            if (!await features.IsEnabledAsync(feature, ct)
                || !await access.HasAsync(permission.ResourceKind, permission.ResourceCode, permission.ActionCode, ct))
            {
                continue;
            }

            var target = ReadTarget(item.MetadataJson);
            if (target is null || target.Value.Id != item.EntityId
                || (short)ContentAudit.ParentKind(target.Value) != item.EntityKind)
            {
                continue;
            }

            if (allowedTargets.Add((target.Value, attachment)))
                await contentAccess.RequireAsync(target.Value, permission, ct);

            items.Add(item);
        }

        // The original cursor advances over filtered events as well as visible ones.
        return page with { Items = items };
    }

    private static BusinessObjectRef? ReadTarget(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
            return null;

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("target", out var target)
                ? target.Deserialize<BusinessObjectRef>(Json)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
