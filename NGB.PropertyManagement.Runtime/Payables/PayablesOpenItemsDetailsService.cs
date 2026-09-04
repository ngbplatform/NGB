using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Core.Catalogs.Exceptions;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Payables;

public sealed class PayablesOpenItemsDetailsService(
    IPayablesOpenItemsService openItems,
    ICatalogService catalogs,
    IPropertyManagementDocumentReaders readers,
    IUnitOfWork uow)
    : IPayablesOpenItemsDetailsService
{
    public async Task<PayablesOpenItemsDetailsResponse> GetOpenItemsDetailsPageAsync(
        Guid partyId,
        Guid propertyId,
        DateOnly? asOfMonth,
        DateOnly? toMonth,
        int chargeOffset,
        int creditOffset,
        int allocationOffset,
        int limit,
        CancellationToken ct = default)
    {
        ValidateOffset(chargeOffset, nameof(chargeOffset));
        ValidateOffset(creditOffset, nameof(creditOffset));
        ValidateOffset(allocationOffset, nameof(allocationOffset));
        ValidateLimit(limit);

        var full = await GetOpenItemsDetailsAsync(partyId, propertyId, asOfMonth, toMonth, ct);

        return full with
        {
            Charges = full.Charges.Skip(chargeOffset).Take(limit).ToArray(),
            Credits = full.Credits.Skip(creditOffset).Take(limit).ToArray(),
            Allocations = full.Allocations.AsEnumerable().Reverse().Skip(allocationOffset).Take(limit).ToArray(),
            ChargeCount = full.Charges.Count,
            CreditCount = full.Credits.Count,
            AllocationCount = full.Allocations.Count,
            ChargeOffset = chargeOffset,
            CreditOffset = creditOffset,
            AllocationOffset = allocationOffset,
            Limit = limit,
            ChargesHaveMore = chargeOffset + limit < full.Charges.Count,
            CreditsHaveMore = creditOffset + limit < full.Credits.Count,
            AllocationsHaveMore = allocationOffset + limit < full.Allocations.Count,
        };
    }

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

    private static void ValidateOffset(int offset, string offsetName)
    {
        if (offset is < 0 or > PagingLimits.MaxOffset)
            throw new NgbArgumentOutOfRangeException(offsetName, offset, $"Offset must be between 0 and {PagingLimits.MaxOffset}.");
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is <= 0 or > PagingLimits.MaxPageSize)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, $"Limit must be between 1 and {PagingLimits.MaxPageSize}.");
    }
}
