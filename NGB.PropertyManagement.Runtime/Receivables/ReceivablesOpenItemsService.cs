using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Core.Dimensions;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PropertyManagement.Runtime.Receivables;

/// <summary>
/// Computes current receivables open items (charges outstanding and available credits)
/// for a lease (party/property can be omitted and are derived from the lease).
///
/// Implementation notes:
/// - Reads Operational Register movements (pm.receivables_open_items) and aggregates net amount per receivable_item.
/// - Storno rows are treated as sign inversions.
/// - Uses lease start month as the default lower bound to avoid scanning unrelated history.
/// </summary>
public sealed class ReceivablesOpenItemsService(
    IPropertyManagementAccountingPolicyReader policyReader,
    IDocumentService documents,
    IOperationalRegisterMovementsQueryReader movements,
    IDocumentDisplayReader documentDisplayReader)
    : IReceivablesOpenItemsService
{
    private const int PageSize = 5000;

    public async Task<ReceivablesOpenItemsResponse> GetOpenItemsAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        CancellationToken ct = default)
    {
        if (leaseId == Guid.Empty)
            throw ReceivablesRequestValidationException.LeaseRequired();

        var policy = await policyReader.GetRequiredAsync(ct);

        // Use lease start month as the scan lower bound (works well in production and avoids global scans).
        DateOnly leaseStart;
        Guid leasePrimaryPartyId;
        Guid leasePropertyId;
        try
        {
            var lease = await documents.GetByIdAsync(PropertyManagementCodes.Lease, leaseId, ct);
            leaseStart = ReadDateOnly(lease.Payload, "start_on_utc");

            leasePrimaryPartyId = ReadPrimaryPartyIdRequired(lease.Payload);
            leasePropertyId = ReadGuid(lease.Payload, "property_id");

            if (partyId == Guid.Empty)
                partyId = leasePrimaryPartyId;
            else if (partyId != leasePrimaryPartyId)
                throw ReceivablesOpenItemsQueryValidationException.PartyMismatch(leaseId, leasePrimaryPartyId, partyId);

            if (propertyId == Guid.Empty)
                propertyId = leasePropertyId;
            else if (propertyId != leasePropertyId)
                throw ReceivablesOpenItemsQueryValidationException.PropertyMismatch(leaseId, leasePropertyId, propertyId);
        }
        catch (DocumentNotFoundException)
        {
            // UI/report scenarios may query with arbitrary ids. Treat missing lease as empty report, not an error.
            return new ReceivablesOpenItemsResponse(
                RegisterId: policy.ReceivablesOpenItemsOperationalRegisterId,
                Charges: [],
                Credits: [],
                TotalOutstanding: 0m,
                TotalCredit: 0m);
        }

        var leaseStartMonth = new DateOnly(leaseStart.Year, leaseStart.Month, 1);
        var nowMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var fromMonth = leaseStartMonth <= nowMonth ? leaseStartMonth : nowMonth;

        var partyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}");
        var propertyDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}");
        var leaseDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}");
        var itemDimId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");

        var filter = new List<DimensionValue>(4)
        {
            new(partyDimId, partyId),
            new(propertyDimId, propertyId),
            new(leaseDimId, leaseId)
        };

        // Future-start leases can still have movements dated in the current month.
        // Use the current month as a safe baseline and extend upward when future-dated rows exist.
        var toMonth = await OperationalRegisterScanBoundaries.ResolveToMonthInclusiveAsync(
            movements,
            policy.ReceivablesOpenItemsOperationalRegisterId,
            fromMonth,
            nowMonth,
            dimensions: filter,
            ct: ct);

        var netByItem = new Dictionary<Guid, decimal>();
        var displayByItem = new Dictionary<Guid, string?>();

        long? after = null;

        while (true)
        {
            var page = await movements.GetByMonthsAsync(
                policy.ReceivablesOpenItemsOperationalRegisterId,
                fromMonth,
                toMonth,
                dimensions: filter,
                afterMovementId: after,
                limit: PageSize,
                ct: ct);

            if (page.Count == 0)
                break;

            foreach (var row in page)
            {
                if (!TryGetValueId(row.Dimensions, itemDimId, out var itemId) || itemId == Guid.Empty)
                    continue; // malformed row; ignore

                var amount = ReadSingleAmount(row.Values);
                if (amount == 0m)
                    continue;

                var signed = row.IsStorno ? -amount : amount;

                netByItem.TryGetValue(itemId, out var existing);
                netByItem[itemId] = existing + signed;

                if (!displayByItem.ContainsKey(itemId))
                {
                    displayByItem[itemId] = row.DimensionValueDisplays.GetValueOrDefault(itemDimId);
                }
            }

            after = page[^1].MovementId;
            if (page.Count < PageSize)
                break;
        }

        var charges = new List<ReceivablesOpenItemDto>();
        var credits = new List<ReceivablesOpenItemDto>();
        var documentRefs = netByItem.Count == 0
            ? new Dictionary<Guid, DocumentDisplayRef>()
            : new Dictionary<Guid, DocumentDisplayRef>(await documentDisplayReader.ResolveRefsAsync(netByItem.Keys.ToArray(), ct));

        var totalOutstanding = 0m;
        var totalCredit = 0m;

        foreach (var (itemId, net) in netByItem)
        {
            if (net == 0m)
                continue;

            displayByItem.TryGetValue(itemId, out var display);
            documentRefs.TryGetValue(itemId, out var documentRef);
            var resolvedDisplay = documentRef?.Display ?? display;
            var documentType = string.IsNullOrWhiteSpace(documentRef?.TypeCode) ? null : documentRef.TypeCode;

            if (net > 0m)
            {
                charges.Add(new ReceivablesOpenItemDto(itemId, resolvedDisplay, net, documentType));
                totalOutstanding += net;
            }
            else
            {
                var credit = -net;
                credits.Add(new ReceivablesOpenItemDto(itemId, resolvedDisplay, credit, documentType));
                totalCredit += credit;
            }
        }

        // Stable ordering for UI.
        charges.Sort(static (a, b) => a.ItemId.CompareTo(b.ItemId));
        credits.Sort(static (a, b) => a.ItemId.CompareTo(b.ItemId));

        return new ReceivablesOpenItemsResponse(
            RegisterId: policy.ReceivablesOpenItemsOperationalRegisterId,
            Charges: charges,
            Credits: credits,
            TotalOutstanding: totalOutstanding,
            TotalCredit: totalCredit);
    }

    private static Guid ReadPrimaryPartyIdRequired(RecordPayload payload)
    {
        if (payload.Parts is null || payload.Parts.Count == 0)
            throw new NgbConfigurationViolationException(
                $"'{PropertyManagementCodes.Lease}' payload must include parts to resolve the primary party.");

        if (!payload.Parts.TryGetValue("parties", out var parties))
            throw new NgbConfigurationViolationException(
                $"'{PropertyManagementCodes.Lease}' payload must include part 'parties' to resolve the primary party.");

        var primary = parties.Rows.SingleOrDefault(r =>
            r.TryGetValue("is_primary", out var p) && p.ValueKind == JsonValueKind.True);

        if (primary is null)
            throw new NgbConfigurationViolationException(
                $"'{PropertyManagementCodes.Lease}' must have exactly one primary party (none found).");

        if (!primary.TryGetValue("party_id", out var idEl))
            throw new NgbConfigurationViolationException(
                $"'{PropertyManagementCodes.Lease}' primary party row must contain 'party_id'.");

        var id = idEl.ParseGuidOrRef();
        if (id == Guid.Empty)
            throw new NgbConfigurationViolationException(
                $"'{PropertyManagementCodes.Lease}' primary party_id must be non-empty.");

        return id;
    }

    private static Guid ReadGuid(RecordPayload payload, string field)
    {
        if (payload.Fields is null || !payload.Fields.TryGetValue(field, out var el))
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
    {
        if (values.Count == 0)
            return 0m;

        // Open-items register is expected to have a single resource: "amount".
        if (values.TryGetValue("amount", out var v))
            return v;

        // Be tolerant in case resource column_code changes.
        return values.Values.FirstOrDefault();
    }

    private static DateOnly ReadDateOnly(RecordPayload payload, string field)
    {
        if (payload.Fields is null || !payload.Fields.TryGetValue(field, out var el))
            throw new NgbConfigurationViolationException($"Required field '{field}' is missing on '{PropertyManagementCodes.Lease}'.");

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s) && DateOnly.TryParse(s, out var d))
                return d;
        }

        throw new NgbConfigurationViolationException($"Field '{field}' on '{PropertyManagementCodes.Lease}' must be a date string.");
    }
}
