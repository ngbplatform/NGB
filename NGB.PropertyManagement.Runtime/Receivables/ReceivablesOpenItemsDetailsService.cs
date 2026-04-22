using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Core.Catalogs.Exceptions;
using NGB.Core.Documents.Exceptions;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Receivables;

/// <summary>
/// UI-friendly receivables open-items read-model with document details.
///
/// Avoids N+1 by loading receivable charge/payment heads and charge types in bulk.
/// </summary>
public sealed class ReceivablesOpenItemsDetailsService(
    IReceivablesOpenItemsService openItems,
    IPropertyManagementAccountingPolicyReader policyReader,
    IDocumentService documents,
    ICatalogService catalogs,
    IPropertyManagementDocumentReaders readers,
    IUnitOfWork uow)
    : IReceivablesOpenItemsDetailsService
{
    public async Task<ReceivablesOpenItemsDetailsResponse> GetOpenItemsDetailsAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        DateOnly? asOfMonth = null,
        DateOnly? toMonth = null,
        CancellationToken ct = default)
    {
        if (leaseId == Guid.Empty)
            throw ReceivablesRequestValidationException.LeaseRequired();

        if (asOfMonth is not null)
            if (asOfMonth.Value.Day != 1)
                throw ReceivablesRequestValidationException.MonthMustBeMonthStart("asOfMonth");

        if (toMonth is not null)
            if (toMonth.Value.Day != 1)
                throw ReceivablesRequestValidationException.MonthMustBeMonthStart("toMonth");

        if (asOfMonth is not null && toMonth is not null && asOfMonth.Value > toMonth.Value)
            throw ReceivablesRequestValidationException.MonthRangeInvalid();

        // Enrich with lease context (primary party/property ids + displays).
        Guid leasePrimaryPartyId;
        Guid leasePropertyId;
        string? leaseDisplay;

        try
        {
            var leaseDoc = await documents.GetByIdAsync(PropertyManagementCodes.Lease, leaseId, ct);
            leaseDisplay = leaseDoc.Display;
            leasePrimaryPartyId = ReadPrimaryPartyIdRequired(leaseDoc.Payload);
            leasePropertyId = ReadGuidRequired(leaseDoc.Payload, "property_id");
        }
        catch (DocumentNotFoundException)
        {
            // Treat missing lease as an empty read-model, not an error.
            var policy = await policyReader.GetRequiredAsync(ct);

            return new ReceivablesOpenItemsDetailsResponse(
                RegisterId: policy.ReceivablesOpenItemsOperationalRegisterId,
                PartyId: Guid.Empty,
                PartyDisplay: null,
                PropertyId: Guid.Empty,
                PropertyDisplay: null,
                LeaseId: leaseId,
                LeaseDisplay: null,
                Charges: [],
                Credits: [],
                Allocations: [],
                TotalOutstanding: 0m,
                TotalCredit: 0m);
        }

        // Optional request filters: partyId/propertyId may be omitted.
        if (partyId != Guid.Empty && partyId != leasePrimaryPartyId)
            throw ReceivablesOpenItemsQueryValidationException.PartyMismatch(leaseId, leasePrimaryPartyId, partyId);

        if (propertyId != Guid.Empty && propertyId != leasePropertyId)
            throw ReceivablesOpenItemsQueryValidationException.PropertyMismatch(leaseId, leasePropertyId, propertyId);

        partyId = partyId == Guid.Empty ? leasePrimaryPartyId : partyId;
        propertyId = propertyId == Guid.Empty ? leasePropertyId : propertyId;

        string? partyDisplay = null;
        string? propertyDisplay = null;

        try
        {
            partyDisplay = (await catalogs.GetByIdAsync(PropertyManagementCodes.Party, partyId, ct)).Display;
        }
        catch (CatalogNotFoundException)
        {
            // keep null
        }

        try
        {
            propertyDisplay = (await catalogs.GetByIdAsync(PropertyManagementCodes.Property, propertyId, ct)).Display;
        }
        catch (CatalogNotFoundException)
        {
            // keep null
        }

        var open = await openItems.GetOpenItemsAsync(partyId, propertyId, leaseId, ct);

        var chargeDocumentIds = open.Charges.Select(x => x.ItemId).ToArray();
        var creditDocumentIds = open.Credits.Select(x => x.ItemId).ToArray();

        var chargeHeads = Array.Empty<PmReceivableChargeHead>();
        var lateFeeChargeHeads = Array.Empty<PmLateFeeChargeHead>();
        var rentChargeHeads = Array.Empty<PmRentChargeHead>();
        var paymentHeads = Array.Empty<PmReceivablePaymentHead>();
        var creditMemoHeads = Array.Empty<PmReceivableCreditMemoHead>();
        var chargeTypeHeads = Array.Empty<PmChargeTypeHead>();
        var allocationsRead = Array.Empty<PmReceivableAllocationRead>();
        var docInfos = Array.Empty<PmDocumentInfo>();

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            chargeHeads = (await readers.ReadReceivableChargeHeadsAsync(chargeDocumentIds, innerCt)).ToArray();
            lateFeeChargeHeads = (await readers.ReadLateFeeChargeHeadsAsync(chargeDocumentIds, innerCt)).ToArray();
            rentChargeHeads = (await readers.ReadRentChargeHeadsAsync(chargeDocumentIds, innerCt)).ToArray();
            paymentHeads = (await readers.ReadReceivablePaymentHeadsAsync(creditDocumentIds, innerCt)).ToArray();
            creditMemoHeads = (await readers.ReadReceivableCreditMemoHeadsAsync(creditDocumentIds, innerCt)).ToArray();
            allocationsRead = (await readers.ReadActiveReceivableAllocationsAsync(partyId, propertyId, leaseId, innerCt)).ToArray();

            var allDocIds = chargeDocumentIds
                .Concat(creditDocumentIds)
                .Concat(allocationsRead.Select(x => x.ApplyId))
                .Distinct()
                .ToArray();
            docInfos = (await readers.ReadDocumentInfosAsync(allDocIds, innerCt)).ToArray();

            var chargeTypeIds = chargeHeads.Select(x => x.ChargeTypeId).Distinct().ToArray();
            chargeTypeHeads = (await readers.ReadChargeTypeHeadsAsync(chargeTypeIds, innerCt)).ToArray();
        }, ct);

        var chargesById = chargeHeads.ToDictionary(x => x.DocumentId);
        var lateFeeChargesById = lateFeeChargeHeads.ToDictionary(x => x.DocumentId);
        var rentChargesById = rentChargeHeads.ToDictionary(x => x.DocumentId);
        var paymentsById = paymentHeads.ToDictionary(x => x.DocumentId);
        var creditMemosById = creditMemoHeads.ToDictionary(x => x.DocumentId);
        var chargeTypesById = chargeTypeHeads.ToDictionary(x => x.ChargeTypeId);
        var docInfosById = docInfos.ToDictionary(x => x.DocumentId);

        var charges = new List<ReceivablesOpenChargeItemDetailsDto>(open.Charges.Count);
        foreach (var x in open.Charges)
        {
            if (!docInfosById.TryGetValue(x.ItemId, out var docInfo))
                continue; // skip orphaned item

            if (string.Equals(docInfo.TypeCode, PropertyManagementCodes.ReceivableCharge, StringComparison.OrdinalIgnoreCase))
            {
                if (!chargesById.TryGetValue(x.ItemId, out var ch))
                    continue;

                chargeTypesById.TryGetValue(ch.ChargeTypeId, out var ctHead);

                charges.Add(new ReceivablesOpenChargeItemDetailsDto(
                    ChargeDocumentId: ch.DocumentId,
                    DocumentType: docInfo.TypeCode,
                    Number: docInfo.Number,
                    ChargeDisplay: x.ItemDisplay,
                    DueOnUtc: ch.DueOnUtc,
                    ChargeTypeId: ch.ChargeTypeId,
                    ChargeTypeDisplay: ctHead?.Display,
                    Memo: ch.Memo,
                    OriginalAmount: ch.Amount,
                    OutstandingAmount: x.Amount));
                continue;
            }

            if (string.Equals(docInfo.TypeCode, PropertyManagementCodes.LateFeeCharge, StringComparison.OrdinalIgnoreCase))
            {
                if (!lateFeeChargesById.TryGetValue(x.ItemId, out var ch))
                    continue;

                charges.Add(new ReceivablesOpenChargeItemDetailsDto(
                    ChargeDocumentId: ch.DocumentId,
                    DocumentType: docInfo.TypeCode,
                    Number: docInfo.Number,
                    ChargeDisplay: x.ItemDisplay,
                    DueOnUtc: ch.DueOnUtc,
                    ChargeTypeId: null,
                    ChargeTypeDisplay: "Late Fee",
                    Memo: ch.Memo,
                    OriginalAmount: ch.Amount,
                    OutstandingAmount: x.Amount));

                continue;
            }

            if (string.Equals(docInfo.TypeCode, PropertyManagementCodes.RentCharge, StringComparison.OrdinalIgnoreCase))
            {
                if (!rentChargesById.TryGetValue(x.ItemId, out var ch))
                    continue;

                charges.Add(new ReceivablesOpenChargeItemDetailsDto(
                    ChargeDocumentId: ch.DocumentId,
                    DocumentType: docInfo.TypeCode,
                    Number: docInfo.Number,
                    ChargeDisplay: x.ItemDisplay,
                    DueOnUtc: ch.DueOnUtc,
                    ChargeTypeId: null,
                    ChargeTypeDisplay: "Rent",
                    Memo: ch.Memo,
                    OriginalAmount: ch.Amount,
                    OutstandingAmount: x.Amount));
            }
        }

        var credits = new List<ReceivablesOpenCreditItemDetailsDto>(open.Credits.Count);
        foreach (var x in open.Credits)
        {
            if (!docInfosById.TryGetValue(x.ItemId, out var docInfo))
                continue;

            if (paymentsById.TryGetValue(x.ItemId, out var payment))
            {
                credits.Add(new ReceivablesOpenCreditItemDetailsDto(
                    CreditDocumentId: payment.DocumentId,
                    DocumentType: docInfo.TypeCode,
                    Number: docInfo.Number,
                    CreditDocumentDisplay: x.ItemDisplay,
                    ReceivedOnUtc: payment.ReceivedOnUtc,
                    Memo: payment.Memo,
                    OriginalAmount: payment.Amount,
                    AvailableCredit: x.Amount));
                continue;
            }

            if (creditMemosById.TryGetValue(x.ItemId, out var creditMemo))
            {
                credits.Add(new ReceivablesOpenCreditItemDetailsDto(
                    CreditDocumentId: creditMemo.DocumentId,
                    DocumentType: docInfo.TypeCode,
                    Number: docInfo.Number,
                    CreditDocumentDisplay: x.ItemDisplay,
                    ReceivedOnUtc: creditMemo.CreditedOnUtc,
                    Memo: creditMemo.Memo,
                    OriginalAmount: creditMemo.Amount,
                    AvailableCredit: x.Amount));
            }
        }

        var allocations = new List<ReceivablesAllocationDetailsDto>(allocationsRead.Length);
        foreach (var x in allocationsRead)
        {
            allocations.Add(new ReceivablesAllocationDetailsDto(
                ApplyId: x.ApplyId,
                ApplyDisplay: x.ApplyDisplay,
                ApplyNumber: x.ApplyNumber,
                CreditDocumentId: x.CreditDocumentId,
                CreditDocumentType: x.CreditDocumentType,
                CreditDocumentDisplay: x.CreditDocumentDisplay,
                CreditDocumentNumber: x.CreditDocumentNumber,
                ChargeDocumentId: x.ChargeDocumentId,
                ChargeDocumentType: x.ChargeDocumentType,
                ChargeDisplay: x.ChargeDisplay,
                ChargeNumber: x.ChargeNumber,
                AppliedOnUtc: x.AppliedOnUtc,
                Amount: x.Amount,
                IsPosted: x.IsPosted));
        }

        if (asOfMonth is not null || toMonth is not null)
        {
            charges = charges.Where(x => IsInMonthRange(x.DueOnUtc, asOfMonth, toMonth)).ToList();
            credits = credits.Where(x => IsInMonthRange(x.ReceivedOnUtc, asOfMonth, toMonth)).ToList();
            allocations = allocations.Where(x => IsInMonthRange(x.AppliedOnUtc, asOfMonth, toMonth)).ToList();
        }

        charges.Sort(static (a, b) =>
        {
            var c = a.DueOnUtc.CompareTo(b.DueOnUtc);
            return c != 0 ? c : a.ChargeDocumentId.CompareTo(b.ChargeDocumentId);
        });

        credits.Sort(static (a, b) =>
        {
            var c = a.ReceivedOnUtc.CompareTo(b.ReceivedOnUtc);
            return c != 0 ? c : a.CreditDocumentId.CompareTo(b.CreditDocumentId);
        });

        allocations.Sort(static (a, b) =>
        {
            var c = a.AppliedOnUtc.CompareTo(b.AppliedOnUtc);
            return c != 0 ? c : a.ApplyId.CompareTo(b.ApplyId);
        });

        var totalOutstanding = charges.Sum(x => x.OutstandingAmount);
        var totalCredit = credits.Sum(x => x.AvailableCredit);

        return new ReceivablesOpenItemsDetailsResponse(
            RegisterId: open.RegisterId,
            PartyId: partyId,
            PartyDisplay: partyDisplay,
            PropertyId: propertyId,
            PropertyDisplay: propertyDisplay,
            LeaseId: leaseId,
            LeaseDisplay: leaseDisplay,
            Charges: charges,
            Credits: credits,
            Allocations: allocations,
            TotalOutstanding: totalOutstanding,
            TotalCredit: totalCredit);
    }

    private static bool IsInMonthRange(DateOnly date, DateOnly? fromMonth, DateOnly? toMonth)
    {
        var month = new DateOnly(date.Year, date.Month, 1);
        if (fromMonth is not null && month < fromMonth.Value)
            return false;
        
        if (toMonth is not null && month > toMonth.Value)
            return false;
        
        return true;
    }

    private static Guid ReadGuidRequired(NGB.Contracts.Common.RecordPayload payload, string field)
    {
        var fields = payload.Fields;
        if (fields is null || !fields.TryGetValue(field, out var el))
            throw new NgbConfigurationViolationException($"Required field '{field}' is missing on '{PropertyManagementCodes.Lease}'.");

        try
        {
            // UI payload enrichment may return reference fields as { id, display }.
            var g = el.ParseGuidOrRef();
            if (g == Guid.Empty)
                throw new NgbConfigurationViolationException("Guid must be non-empty.");

            return g;
        }
        catch (Exception ex)
        {
            throw new NgbConfigurationViolationException(
                $"Field '{field}' on '{PropertyManagementCodes.Lease}' must be a non-empty guid (string or {{id,display}}).",
                new Dictionary<string, object?>
                {
                    ["field"] = field,
                    ["error"] = ex.Message
                });
        }
    }

    private static Guid ReadPrimaryPartyIdRequired(NGB.Contracts.Common.RecordPayload payload)
    {
        if (payload.Parts is null || payload.Parts.Count == 0)
            throw new NgbConfigurationViolationException(
                $"Required part 'parties' is missing on '{PropertyManagementCodes.Lease}'.");

        if (!payload.Parts.TryGetValue("parties", out var parties))
            throw new NgbConfigurationViolationException($"Required part 'parties' is missing on '{PropertyManagementCodes.Lease}'.");

        // Exactly one primary is enforced by DB constraint trigger; still validate defensively.
        IReadOnlyDictionary<string, JsonElement>? primary = null;

        foreach (var r in parties.Rows)
        {
            if (r.TryGetValue("is_primary", out var el) && el.ValueKind == JsonValueKind.True)
            {
                if (primary is not null)
                    throw new NgbConfigurationViolationException($"'{PropertyManagementCodes.Lease}' has more than one primary party row.");

                primary = r;
            }
        }

        if (primary is null)
            throw new NgbConfigurationViolationException($"'{PropertyManagementCodes.Lease}' must have exactly one primary party row.");

        if (!primary.TryGetValue("party_id", out var idEl))
            throw new NgbConfigurationViolationException($"'{PropertyManagementCodes.Lease}' primary party row must contain 'party_id'.");

        var id = idEl.ParseGuidOrRef();
        if (id == Guid.Empty)
            throw new NgbConfigurationViolationException($"'{PropertyManagementCodes.Lease}' primary party_id must be non-empty.");

        return id;
    }
}
