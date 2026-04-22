using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Posting;

/// <summary>
/// Accounting posting for pm.late_fee_charge.
///
/// Semantics:
/// - OccurredAtUtc = due_on_utc (00:00:00Z)
/// - Dr AR Tenants / Cr Late Fee Income
/// - Dimensions: (pm.party, pm.property, pm.lease)
/// </summary>
public sealed class LateFeeChargePostingHandler(
    IPropertyManagementDocumentReaders readers,
    IPropertyManagementAccountingPolicyReader policyReader)
    : IDocumentPostingHandler
{
    public string TypeCode => PropertyManagementCodes.LateFeeCharge;

    public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
    {
        var charge = await readers.ReadLateFeeChargeHeadAsync(document.Id, ct);
        await LeaseConsistencyGuard.EnsureAsync(document.Id, charge.LeaseId, charge.PartyId, charge.PropertyId, readers, ct);
        var policy = await policyReader.GetRequiredAsync(ct);

        var coa = await ctx.GetChartOfAccountsAsync(ct);
        var debit = coa.Get(policy.AccountsReceivableTenantsAccountId);
        var credit = coa.Get(policy.LateFeeIncomeAccountId);

        var period = new DateTime(charge.DueOnUtc.Year, charge.DueOnUtc.Month, charge.DueOnUtc.Day, 0, 0, 0, DateTimeKind.Utc);

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");

        var bag = new DimensionBag([
            new DimensionValue(partyDimId, charge.PartyId),
            new DimensionValue(propertyDimId, charge.PropertyId),
            new DimensionValue(leaseDimId, charge.LeaseId)
        ]);

        ctx.Post(
            documentId: document.Id,
            period: period,
            debit: debit,
            credit: credit,
            amount: charge.Amount,
            debitDimensions: bag,
            creditDimensions: bag);
    }
}
