using NGB.Application.Abstractions.Services;
using NGB.Core.Catalogs.Exceptions;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;

namespace NGB.PropertyManagement.Runtime.Payables;

public sealed class PayablesOpenItemsDetailsService(
    IPayablesOpenItemsService openItems,
    ICatalogService catalogs,
    IPropertyManagementDocumentReaders readers,
    IUnitOfWork uow)
    : IPayablesOpenItemsDetailsService
{
    public async Task<PayablesOpenItemsDetailsResponse> GetOpenItemsDetailsAsync(
        Guid partyId,
        Guid propertyId,
        DateOnly? asOfMonth = null,
        DateOnly? toMonth = null,
        CancellationToken ct = default)
    {
        string? vendorDisplay = null;
        string? propertyDisplay = null;

        try
        {
            vendorDisplay = (await catalogs.GetByIdAsync(PropertyManagementCodes.Party, partyId, ct)).Display;
        }
        catch (CatalogNotFoundException)
        {
            // do nothing
        }

        try
        {
            propertyDisplay = (await catalogs.GetByIdAsync(PropertyManagementCodes.Property, propertyId, ct)).Display;
        }
        catch (CatalogNotFoundException)
        {
            // do nothing
        }

        var open = await openItems.GetOpenItemsAsync(partyId, propertyId, asOfMonth, toMonth, ct);
        var allocationReads = await uow.ExecuteInUowTransactionAsync(
            innerCt => readers.ReadActivePayableAllocationsAsync(partyId, propertyId, asOfMonth, toMonth, innerCt),
            ct);

        var allocations = allocationReads
            .Select(x => new PayablesAllocationDetailsDto(
                x.ApplyId,
                x.ApplyDisplay,
                x.ApplyNumber,
                x.CreditDocumentId,
                x.CreditDocumentType,
                x.CreditDocumentDisplay,
                x.CreditDocumentNumber,
                x.ChargeDocumentId,
                x.ChargeDocumentType,
                x.ChargeDisplay,
                x.ChargeNumber,
                x.AppliedOnUtc,
                x.Amount,
                x.IsPosted))
            .ToList();

        allocations.Sort((a, b)
            => a.AppliedOnUtc != b.AppliedOnUtc ? a.AppliedOnUtc.CompareTo(b.AppliedOnUtc) : a.ApplyId.CompareTo(b.ApplyId));

        return new PayablesOpenItemsDetailsResponse(
            open.RegisterId,
            partyId,
            vendorDisplay,
            propertyId,
            propertyDisplay,
            open.Charges,
            open.Credits,
            allocations,
            open.TotalOutstanding,
            open.TotalCredit);
    }
}
