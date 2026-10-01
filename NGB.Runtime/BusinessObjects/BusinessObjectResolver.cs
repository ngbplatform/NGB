using NGB.Accounting.Documents;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Core.Security;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.BusinessObjects;

internal sealed class BusinessObjectResolver(
    ICatalogService catalogs,
    IDocumentService documents,
    IGeneralJournalEntryUiService journals,
    INgbAccessChecker access)
    : IBusinessObjectResolver
{
    public async Task<ResolvedBusinessObject> ResolveAsync(BusinessObjectRef target, CancellationToken ct)
    {
        if (!Enum.IsDefined(target.Kind))
            throw new BusinessObjectException("business_object.unsupported_kind", "Unsupported business object kind.");

        if (target.Id == Guid.Empty || string.IsNullOrWhiteSpace(target.TypeCode) || target.TypeCode.Length > 200)
            throw new BusinessObjectException("business_object.invalid_reference", "A valid object ID and canonical type code are required.");

        var journal = target.TypeCode == AccountingDocumentTypeCodes.GeneralJournalEntry;
        if ((target.Kind == BusinessObjectKind.GeneralJournalEntry) != journal)
            throw new BusinessObjectException("business_object.invalid_type", "The type code does not match the business object kind.");

        await access.RequireAsync(
            target.Kind == BusinessObjectKind.CatalogItem
                ? NgbResourceKinds.Catalog
                : NgbResourceKinds.Document,
            target.TypeCode,
            NgbPermissionActions.View,
            ct);

        try
        {
            string? display;
            switch (target.Kind)
            {
                case BusinessObjectKind.CatalogItem:
                    RequireCanonical(
                        target.TypeCode,
                        (await catalogs.GetTypeMetadataAsync(target.TypeCode, ct)).CatalogType);
                    display = (await catalogs.GetByIdAsync(target.TypeCode, target.Id, ct)).Display;
                    break;
                case BusinessObjectKind.Document:
                    RequireCanonical(
                        target.TypeCode,
                        (await documents.GetTypeMetadataAsync(target.TypeCode, ct)).DocumentType);
                    display = (await documents.GetByIdAsync(target.TypeCode, target.Id, ct)).Display;
                    break;
                default:
                    await journals.GetByIdAsync(target.Id, ct);
                    display = null;
                    break;
            }

            return new(target, display);
        }
        catch (NgbException ex) when (ex.Kind == NgbErrorKind.NotFound)
        {
            throw new BusinessObjectException("business_object.not_found", "Business object was not found.", NgbErrorKind.NotFound);
        }
    }

    private static void RequireCanonical(string supplied, string canonical)
    {
        if (!string.Equals(supplied, canonical, StringComparison.Ordinal))
            throw new BusinessObjectException("business_object.invalid_type", "Use the canonical business object type code.");
    }
}

internal sealed class BusinessObjectException(string code, string message, NgbErrorKind kind = NgbErrorKind.Validation)
    : NgbException(message, code, kind);
