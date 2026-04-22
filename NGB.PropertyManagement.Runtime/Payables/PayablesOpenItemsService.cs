using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Payables;

public sealed class PayablesOpenItemsService(
    IPropertyManagementAccountingPolicyReader policyReader,
    IOperationalRegisterMovementsQueryReader movements,
    IPropertyManagementDocumentReaders readers,
    IUnitOfWork uow)
    : IPayablesOpenItemsService
{
    private const int PageSize = 5000;

    public async Task<(Guid RegisterId, IReadOnlyList<PayablesOpenChargeItemDetailsDto> Charges, IReadOnlyList<PayablesOpenCreditItemDetailsDto> Credits, decimal TotalOutstanding, decimal TotalCredit)> GetOpenItemsAsync(
        Guid partyId,
        Guid propertyId,
        DateOnly? asOfMonth = null,
        DateOnly? toMonth = null,
        CancellationToken ct = default)
    {
        if (partyId == Guid.Empty)
            throw PayablesRequestValidationException.VendorRequired();

        if (propertyId == Guid.Empty)
            throw PayablesRequestValidationException.PropertyRequired();

        if (asOfMonth is not null && asOfMonth.Value.Day != 1)
            throw PayablesRequestValidationException.MonthMustBeMonthStart("asOfMonth");

        if (toMonth is not null && toMonth.Value.Day != 1)
            throw PayablesRequestValidationException.MonthMustBeMonthStart("toMonth");

        if (asOfMonth is not null && toMonth is not null && asOfMonth.Value > toMonth.Value)
            throw PayablesRequestValidationException.MonthRangeInvalid();

        var policy = await policyReader.GetRequiredAsync(ct);

        return await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            var fromMonth = await readers.ReadFirstPayablesActivityMonthAsync(partyId, propertyId, innerCt)
                            ?? new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

            if (asOfMonth is not null && asOfMonth.Value > fromMonth)
                fromMonth = asOfMonth.Value;

            var nowMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
            var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
            var dims = new List<DimensionValue>
            {
                new(partyDimId, partyId),
                new(propertyDimId, propertyId)
            };

            var resolvedTo = await OperationalRegisterScanBoundaries.ResolveToMonthInclusiveAsync(
                movements,
                policy.PayablesOpenItemsOperationalRegisterId,
                fromMonth,
                nowMonth,
                dimensions: dims,
                ct: innerCt);

            if (toMonth is not null && toMonth.Value < resolvedTo)
                resolvedTo = toMonth.Value;

            var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");
            var netByItem = new Dictionary<Guid, decimal>();
            var displayByItem = new Dictionary<Guid, string?>();
            long? after = null;

            while (true)
            {
                var page = await movements.GetByMonthsAsync(
                    policy.PayablesOpenItemsOperationalRegisterId,
                    fromMonth,
                    resolvedTo,
                    dimensions: dims,
                    afterMovementId: after,
                    limit: PageSize,
                    ct: innerCt);

                if (page.Count == 0)
                    break;

                foreach (var row in page)
                {
                    if (!TryGetValueId(row.Dimensions, itemDimId, out var itemId) || itemId == Guid.Empty)
                        continue;

                    var amount = ReadSingleAmount(row.Values);
                    if (amount == 0m)
                        continue;

                    var signed = row.IsStorno ? -amount : amount;
                    netByItem[itemId] = netByItem.TryGetValue(itemId, out var existing) ? existing + signed : signed;

                    if (!displayByItem.ContainsKey(itemId))
                        displayByItem[itemId] = row.DimensionValueDisplays.GetValueOrDefault(itemDimId);
                }

                after = page[^1].MovementId;
                if (page.Count < PageSize)
                    break;
            }

            var chargeIds = netByItem
                .Where(x => x.Value > 0m)
                .Select(x => x.Key)
                .ToArray();
            var creditIds = netByItem
                .Where(x => x.Value < 0m)
                .Select(x => x.Key).
                ToArray();
            var allIds = chargeIds
                .Concat(creditIds)
                .Distinct()
                .ToArray();

            var infos = (await readers.ReadDocumentInfosAsync(allIds, innerCt))
                .ToDictionary(x => x.DocumentId);
            var charges = (await readers.ReadPayableChargeHeadsAsync(chargeIds, innerCt))
                .ToDictionary(x => x.DocumentId);
            var payments = (await readers.ReadPayablePaymentHeadsAsync(creditIds, innerCt))
                .ToDictionary(x => x.DocumentId);
            var creditMemos = (await readers.ReadPayableCreditMemoHeadsAsync(creditIds, innerCt))
                .ToDictionary(x => x.DocumentId);
            var chargeTypeIds = charges.Values
                .Select(x => x.ChargeTypeId)
                .Distinct()
                .ToArray();
            var chargeTypes = (await readers.ReadPayableChargeTypeHeadsAsync(chargeTypeIds, innerCt))
                .ToDictionary(x => x.ChargeTypeId);

            var chargeDtos = new List<PayablesOpenChargeItemDetailsDto>();
            var creditDtos = new List<PayablesOpenCreditItemDetailsDto>();
            var totalOutstanding = 0m;
            var totalCredit = 0m;

            foreach (var (itemId, net) in netByItem)
            {
                if (net == 0m || !infos.TryGetValue(itemId, out var info))
                    continue;

                displayByItem.TryGetValue(itemId, out var display);

                if (net > 0m && charges.TryGetValue(itemId, out var ch))
                {
                    chargeTypes.TryGetValue(ch.ChargeTypeId, out var ctHead);
                    chargeDtos.Add(new PayablesOpenChargeItemDetailsDto(
                        itemId,
                        info.TypeCode,
                        info.Number,
                        display,
                        ch.DueOnUtc,
                        ch.ChargeTypeId,
                        ctHead?.Display,
                        ch.VendorInvoiceNo,
                        ch.Memo,
                        ch.Amount,
                        net));
                    totalOutstanding += net;
                }
                else if (net < 0m)
                {
                    if (payments.TryGetValue(itemId, out var payment))
                    {
                        creditDtos.Add(new PayablesOpenCreditItemDetailsDto(
                            itemId,
                            info.TypeCode,
                            info.Number,
                            display,
                            payment.PaidOnUtc,
                            payment.Memo,
                            payment.Amount,
                            -net));
                        totalCredit += -net;
                    }
                    else if (creditMemos.TryGetValue(itemId, out var creditMemo))
                    {
                        creditDtos.Add(new PayablesOpenCreditItemDetailsDto(
                            itemId,
                            info.TypeCode,
                            info.Number,
                            display,
                            creditMemo.CreditedOnUtc,
                            creditMemo.Memo,
                            creditMemo.Amount,
                            -net));
                        totalCredit += -net;
                    }
                }
            }

            chargeDtos.Sort((a, b)
                => a.DueOnUtc != b.DueOnUtc
                    ? a.DueOnUtc.CompareTo(b.DueOnUtc)
                    : a.ChargeDocumentId.CompareTo(b.ChargeDocumentId));

            creditDtos.Sort((a, b)
                => a.CreditDocumentDateUtc != b.CreditDocumentDateUtc
                    ? a.CreditDocumentDateUtc.CompareTo(b.CreditDocumentDateUtc)
                    : a.CreditDocumentId.CompareTo(b.CreditDocumentId));

            return (
                policy.PayablesOpenItemsOperationalRegisterId,
                (IReadOnlyList<PayablesOpenChargeItemDetailsDto>)chargeDtos,
                (IReadOnlyList<PayablesOpenCreditItemDetailsDto>)creditDtos,
                totalOutstanding,
                totalCredit);
        }, ct);
    }

    private static bool TryGetValueId(DimensionBag bag, Guid dimensionId, out Guid valueId)
    {
        foreach (var x in bag)
        {
            if (x.DimensionId == dimensionId)
            {
                valueId = x.ValueId;
                return true;
            }
        }

        valueId = Guid.Empty;
        return false;
    }

    private static decimal ReadSingleAmount(IReadOnlyDictionary<string, decimal> values)
        => values.TryGetValue("amount", out var v) ? v : values.Values.FirstOrDefault();
}
