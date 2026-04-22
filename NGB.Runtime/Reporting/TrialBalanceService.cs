using NGB.Accounting.Reports.TrialBalance;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Readers.Reports;
using IDimensionValueEnrichmentReader = NGB.Persistence.Dimensions.Enrichment.IDimensionValueEnrichmentReader;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Application-level reporting service that materializes Trial Balance rows
/// from a specialized aggregated snapshot reader.
/// </summary>
public sealed class TrialBalanceService(
    ITrialBalanceSnapshotReader snapshotReader,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : ITrialBalanceReader
{
    public Task<IReadOnlyList<TrialBalanceRow>> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default)
        => GetAsync(fromInclusive, toInclusive, dimensionScopes: null, ct);

    public async Task<IReadOnlyList<TrialBalanceRow>> GetAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes,
        CancellationToken ct = default)
    {
        var snapshot = await snapshotReader.GetAsync(fromInclusive, toInclusive, dimensionScopes, ct);
        if (snapshot.Rows.Count == 0)
            return [];

        var dimensionSetIds = snapshot.Rows
            .Select(x => x.DimensionSetId)
            .Distinct()
            .ToArray();

        var bagsById = await dimensionSetReader.GetBagsByIdsAsync(dimensionSetIds, ct);
        var valueKeys = bagsById.Values
            .Where(x => !x.IsEmpty)
            .CollectValueKeys();
        var displaysByValue = valueKeys.Count == 0
            ? new Dictionary<DimensionValueKey, string>()
            : await dimensionValueEnrichmentReader.ResolveAsync(valueKeys, ct);

        return snapshot.Rows
            .Select(row =>
            {
                var bag = bagsById.TryGetValue(row.DimensionSetId, out var existingBag)
                    ? existingBag
                    : DimensionBag.Empty;

                return new TrialBalanceRow
                {
                    AccountId = row.AccountId,
                    AccountCode = row.AccountCode,
                    DimensionSetId = row.DimensionSetId,
                    Dimensions = bag,
                    DimensionValueDisplays = bag.ToValueDisplayMap(displaysByValue),
                    OpeningBalance = row.OpeningBalance,
                    DebitAmount = row.DebitAmount,
                    CreditAmount = row.CreditAmount,
                    ClosingBalance = row.OpeningBalance + (row.DebitAmount - row.CreditAmount)
                };
            })
            .ToList();
    }
}
