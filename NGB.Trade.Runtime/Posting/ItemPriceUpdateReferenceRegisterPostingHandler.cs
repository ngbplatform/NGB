using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Trade.Documents;
using NGB.Tools.Extensions;

namespace NGB.Trade.Runtime.Posting;

/// <summary>
/// Writes current price state into the Trade item-prices RR.
///
/// Design note:
/// - The platform dimension model is GUID-based, so the US-market single-currency MVP keeps
///   currency as a register field rather than a dimension. This stays aligned with the roadmap's
///   simplified no-multi-currency scope while preserving the ability to extend later.
/// </summary>
public sealed class ItemPriceUpdateReferenceRegisterPostingHandler(
    ITradeDocumentReaders readers,
    IDimensionSetService dimensionSets)
    : IDocumentReferenceRegisterPostingHandler
{
    public string TypeCode => TradeCodes.ItemPriceUpdate;

    public async Task BuildRecordsAsync(
        DocumentRecord document,
        ReferenceRegisterWriteOperation operation,
        IReferenceRegisterRecordsBuilder builder,
        CancellationToken ct)
    {
        var head = await readers.ReadItemPriceUpdateHeadAsync(document.Id, ct);
        var lines = await readers.ReadItemPriceUpdateLinesAsync(document.Id, ct);
        var updatedAtUtc = DateTime.UtcNow;

        foreach (var line in lines)
        {
            var currency = string.IsNullOrWhiteSpace(line.Currency)
                ? TradeCodes.DefaultCurrency
                : line.Currency.Trim().ToUpperInvariant();
            var bag = new DimensionBag(
            [
                new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.Item}"), line.ItemId),
                new DimensionValue(DeterministicGuid.Create($"Dimension|{TradeCodes.PriceType}"), line.PriceTypeId)
            ]);

            var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(bag, ct);

            builder.Add(
                TradeCodes.ItemPricesRegisterCode,
                new ReferenceRegisterRecordWrite(
                    DimensionSetId: dimensionSetId,
                    PeriodUtc: null,
                    RecorderDocumentId: null,
                    Values: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["currency"] = currency,
                        ["unit_price"] = line.UnitPrice,
                        ["effective_date"] = head.EffectiveDate,
                        ["source_document_id"] = document.Id,
                        ["updated_at_utc"] = updatedAtUtc
                    },
                    IsDeleted: operation == ReferenceRegisterWriteOperation.Unpost));
        }
    }
}
