using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Accounting posting for pm.rent_charge.
///
/// Agreed:
/// - OccurredAtUtc = due_on_utc (00:00:00Z)
/// - Dr AR Tenants / Cr Rental Income
/// - Dimensions: (pm.party, pm.property, pm.lease)
///
/// Accounts and OR register are resolved from pm.accounting_policy.
/// </summary>
public sealed class RentChargePostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => PropertyManagementCodes.RentCharge;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var rent = await readers.ReadRentChargeHeadAsync(document.Id, ct);
        var lease = await readers.ReadLeaseHeadAsync(rent.LeaseId, ct);
        var policy = await policyReader.GetRequiredAsync(ct);
        
        var coa = await ctx.GetChartOfAccountsAsync(ct);
        var debit = coa.Get(policy.AccountsReceivableTenantsAccountId);
        var credit = coa.Get(policy.RentalIncomeAccountId);

        // OccurredAtUtc (posting period): due date at midnight UTC.
        var period = new DateTime(rent.DueOnUtc.Year, rent.DueOnUtc.Month, rent.DueOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, lease.PrimaryPartyId),
            new DimensionValue(propertyDimId, lease.PropertyId),
            new DimensionValue(leaseDimId, rent.LeaseId)
        ]);

        ctx.Post(
            documentId: document.Id,
            period: period,
            debit: debit,
            credit: credit,
            amount: rent.Amount,
            debitDimensions: bag,
            creditDimensions: bag);
    }
}
