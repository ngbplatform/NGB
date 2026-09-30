using NGB.Contracts.Security;
using NGB.Core.Security;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;

namespace NGB.Runtime.Security;

public sealed class MetadataPermissionDefinitionSource(IDocumentTypeRegistry documents, ICatalogTypeRegistry catalogs)
    : INgbPermissionDefinitionSource
{
    private static readonly string[] DocumentActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Lookup,
        NgbPermissionActions.Create,
        NgbPermissionActions.EditDraft,
        NgbPermissionActions.DeleteDraft,
        NgbPermissionActions.MarkForDeletion,
        NgbPermissionActions.UnmarkForDeletion,
        NgbPermissionActions.Post,
        NgbPermissionActions.Unpost,
        NgbPermissionActions.Repost,
        NgbPermissionActions.ViewEffects,
        NgbPermissionActions.ViewFlow,
        NgbPermissionActions.ViewAudit,
        NgbPermissionActions.Print
    ];

    private static readonly string[] CatalogActions =
    [
        NgbPermissionActions.View,
        NgbPermissionActions.Lookup,
        NgbPermissionActions.Create,
        NgbPermissionActions.Edit,
        NgbPermissionActions.MarkForDeletion,
        NgbPermissionActions.UnmarkForDeletion,
        NgbPermissionActions.ViewAudit
    ];

    public Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
    {
        var result = new List<PermissionDefinitionDto>();

        foreach (var document in documents.GetAll().OrderBy(x => x.TypeCode, StringComparer.OrdinalIgnoreCase))
        {
            var display = document.Presentation?.DisplayName ?? document.TypeCode;
            result.AddRange(DocumentActions.Select(action => new PermissionDefinitionDto(
                NgbResourceKinds.Document,
                document.TypeCode,
                action,
                $"{display}: {Label(action)}",
                "Documents")));
        }

        foreach (var catalog in catalogs.All().OrderBy(x => x.CatalogCode, StringComparer.OrdinalIgnoreCase))
        {
            result.AddRange(CatalogActions.Select(action => new PermissionDefinitionDto(
                NgbResourceKinds.Catalog,
                catalog.CatalogCode,
                action,
                $"{catalog.DisplayName}: {Label(action)}",
                "Catalogs")));
        }

        return Task.FromResult<IReadOnlyList<PermissionDefinitionDto>>(result);
    }

    private static string Label(string action)
        => action
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static x => char.ToUpperInvariant(x[0]) + x[1..])
            .Aggregate((a, b) => $"{a} {b}");
}
