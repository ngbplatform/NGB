using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Core.Dimensions;
using NGB.Core.Documents.Exceptions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Reporting;
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
    internal const int MaxMaterializedOpenItems = 5_000;

    public async Task<ReceivablesOpenItemsPageResponse> GetOpenItemsPageAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        int offset,
        int limit,
        CancellationToken ct = default)
        => await GetOpenItemsPageCoreAsync(
            partyId,
            propertyId,
            leaseId,
            offset,
            limit,
            cursor: null,
            useCursorPaging: false,
            ct);

    public async Task<ReceivablesOpenItemsPageResponse> GetOpenItemsCursorPageAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => await GetOpenItemsPageCoreAsync(
            partyId,
            propertyId,
            leaseId,
            offset: 0,
            limit,
            cursor,
            useCursorPaging: true,
            ct);

    private async Task<ReceivablesOpenItemsPageResponse> GetOpenItemsPageCoreAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        int offset,
        int limit,
        string? cursor,
        bool useCursorPaging,
        CancellationToken ct)
    {
        if (leaseId == Guid.Empty)
            throw ReceivablesRequestValidationException.LeaseRequired();

        if (offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(offset), offset, "Offset must be zero or greater.");

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than zero.");

        var policy = await policyReader.GetRequiredAsync(ct);
        DateOnly leaseStart;

        try
        {
            var lease = await documents.GetByIdAsync(PropertyManagementCodes.Lease, leaseId, ct);
            leaseStart = ReadDateOnly(lease.Payload, "start_on_utc");
            var leasePrimaryPartyId = ReadPrimaryPartyIdRequired(lease.Payload);
            var leasePropertyId = ReadGuid(lease.Payload, "property_id");

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
            return new ReceivablesOpenItemsPageResponse(
                policy.ReceivablesOpenItemsOperationalRegisterId,
                [],
                0,
                0m,
                0m);
        }

        var leaseStartMonth = new DateOnly(leaseStart.Year, leaseStart.Month, 1);
        var nowMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var fromMonth = leaseStartMonth <= nowMonth ? leaseStartMonth : nowMonth;
        var filter = new List<DimensionValue>(3)
        {
            new(DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Party}"), partyId),
            new(DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Property}"), propertyId),
            new(DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.Lease}"), leaseId)
        };
        var toMonth = await OperationalRegisterScanBoundaries.ResolveToMonthInclusiveAsync(
            movements,
            policy.ReceivablesOpenItemsOperationalRegisterId,
            fromMonth,
            nowMonth,
            dimensions: filter,
            ct: ct);
        var itemDimensionId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");
        OperationalRegisterDimensionResourceNetPage page;
        var effectiveOffset = offset;
        string? cursorKind = null;
        OperationalRegisterDimensionResourceNetCursor? decodedCursor = null;

        if (useCursorPaging)
        {
            cursorKind = SpecializedReportCursorCodec.BuildKind(
                "pm.receivables.open-items",
                policy.ReceivablesOpenItemsOperationalRegisterId.ToString("N"),
                partyId.ToString("N"),
                propertyId.ToString("N"),
                leaseId.ToString("N"),
                toMonth.ToString("yyyy-MM-dd"));
            decodedCursor = string.IsNullOrWhiteSpace(cursor)
                ? null
                : SpecializedReportCursorCodec.Decode<OperationalRegisterDimensionResourceNetCursor>(cursorKind, cursor);
            effectiveOffset = decodedCursor?.NextOffset ?? 0;
            page = await movements.GetResourceBalancesByDimensionCursorAsync(
                policy.ReceivablesOpenItemsOperationalRegisterId,
                toMonth,
                filter,
                itemDimensionId,
                "amount",
                decodedCursor,
                limit,
                ct);
        }
        else
        {
            page = await movements.GetResourceBalancesByDimensionPageAsync(
                policy.ReceivablesOpenItemsOperationalRegisterId,
                toMonth,
                filter,
                itemDimensionId,
                "amount",
                offset,
                limit,
                ct);
        }

        var documentRefs = page.Rows.Count == 0
            ? new Dictionary<Guid, DocumentDisplayRef>()
            : new Dictionary<Guid, DocumentDisplayRef>(
                await documentDisplayReader.ResolveRefsAsync(page.Rows.Select(static row => row.ValueId).ToArray(), ct));
        var rows = page.Rows.Select(row =>
        {
            documentRefs.TryGetValue(row.ValueId, out var documentRef);
            var net = row.NetAmount;
            return new ReceivablesOpenItemPageRow(
                IsCharge: net > 0m,
                ItemId: row.ValueId,
                ItemDisplay: documentRef?.Display ?? row.Display,
                Amount: Math.Abs(net),
                DocumentType: string.IsNullOrWhiteSpace(documentRef?.TypeCode) ? null : documentRef.TypeCode);
        }).ToArray();
        var nextCursor = useCursorPaging && page.HasMore && page.Rows.Count > 0
            ? SpecializedReportCursorCodec.Encode(
                cursorKind!,
                new OperationalRegisterDimensionResourceNetCursor(
                    page.Rows[^1].NetAmount > 0m,
                    page.Rows[^1].ValueId,
                    effectiveOffset + page.Rows.Count,
                    page.Total,
                    page.TotalPositive,
                    page.TotalNegativeAbsolute))
            : null;

        return new ReceivablesOpenItemsPageResponse(
            policy.ReceivablesOpenItemsOperationalRegisterId,
            rows,
            page.Total,
            page.TotalPositive,
            page.TotalNegativeAbsolute,
            effectiveOffset,
            page.HasMore,
            nextCursor);
    }

    public async Task<ReceivablesOpenItemsResponse> GetOpenItemsAsync(
        Guid partyId,
        Guid propertyId,
        Guid leaseId,
        CancellationToken ct = default)
    {
        var page = await GetOpenItemsPageAsync(
            partyId,
            propertyId,
            leaseId,
            offset: 0,
            limit: MaxMaterializedOpenItems,
            ct);

        if (page.Total > MaxMaterializedOpenItems)
            throw new OpenItemsResultLimitExceededException(page.Total, MaxMaterializedOpenItems);

        var charges = new List<ReceivablesOpenItemDto>();
        var credits = new List<ReceivablesOpenItemDto>();

        foreach (var row in page.Rows)
        {
            var item = new ReceivablesOpenItemDto(
                row.ItemId,
                row.ItemDisplay,
                row.Amount,
                row.DocumentType);

            if (row.IsCharge)
            {
                charges.Add(item);
            }
            else
            {
                credits.Add(item);
            }
        }

        // Stable ordering for UI.
        charges.Sort(static (a, b) => a.ItemId.CompareTo(b.ItemId));
        credits.Sort(static (a, b) => a.ItemId.CompareTo(b.ItemId));

        return new ReceivablesOpenItemsResponse(
            RegisterId: page.RegisterId,
            Charges: charges,
            Credits: credits,
            TotalOutstanding: page.TotalOutstanding,
            TotalCredit: page.TotalCredit);
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
        // ReadDateOnly has already established that lease scalar fields are present.
        if (!payload.Fields!.TryGetValue(field, out var el))
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

    private static DateOnly ReadDateOnly(RecordPayload payload, string field)
    {
        if (payload.Fields is null || !payload.Fields.TryGetValue(field, out var el))
            throw new NgbConfigurationViolationException($"Required field '{field}' is missing on '{PropertyManagementCodes.Lease}'.");

        if (el.ValueKind == JsonValueKind.String)
        {
            if (DateOnly.TryParse(el.GetString()!, out var d))
                return d;
        }

        throw new NgbConfigurationViolationException($"Field '{field}' on '{PropertyManagementCodes.Lease}' must be a date string.");
    }
}
