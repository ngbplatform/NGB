using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.References;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.AgencyBilling.Runtime.Validation;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Runtime.Locks;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Documents.Validation;

public sealed class SalesInvoicePostValidator(
    IAgencyBillingDocumentReaders readers,
    IAgencyBillingInvoiceUsageReader invoiceUsageReader,
    IAgencyBillingReferenceReaders references,
    IDocumentRepository documents,
    IAdvisoryLockManager locks)
    : IDocumentPostValidator
{
    public string TypeCode => AgencyBillingCodes.SalesInvoice;

    public async Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
    {
        AgencyBillingDocumentValidatorBindingGuard.EnsureExpectedType(documentForUpdate, TypeCode, nameof(SalesInvoicePostValidator));

        var head = await readers.ReadSalesInvoiceHeadAsync(documentForUpdate.Id, ct);
        var lines = await readers.ReadSalesInvoiceLinesAsync(documentForUpdate.Id, ct);

        await AgencyBillingCatalogValidationGuards.EnsureClientAsync(head.ClientId, "client_id", references, ct, requireOperationallyActive: true);
        var project = await AgencyBillingCatalogValidationGuards.EnsureProjectAsync(head.ProjectId, "project_id", references, ct, requireOperationallyActive: true);
        AgencyBillingCatalogValidationGuards.EnsureProjectBelongsToClient(project, head.ClientId, "project_id", "client_id");

        if (head.ContractId is { } contractId && contractId != Guid.Empty)
            await ValidateContractAsync(contractId, head, ct);

        if (lines.Count == 0)
            throw new NgbArgumentInvalidException("lines", "Sales Invoice must contain at least one line.");

        var referencedTimesheetIds = lines
            .Where(x => x.SourceTimesheetId is { } sourceTimesheetId && sourceTimesheetId != Guid.Empty)
            .Select(x => x.SourceTimesheetId!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        await locks.LockDocumentsDeterministicallyAsync(referencedTimesheetIds, ct);

        var serviceItems = await AgencyBillingCatalogValidationGuards.LoadServiceItemsAsync(
            lines.Select(static line => line.ServiceItemId.GetValueOrDefault()),
            references,
            ct);
        var sourceDocuments = await documents.GetByIdsAsync(referencedTimesheetIds, ct);
        var sourceTimesheets = await readers.ReadTimesheetHeadsAsync(referencedTimesheetIds, ct);

        var expectedAmount = 0m;
        var currentUsageByTimesheet = new Dictionary<Guid, (decimal Hours, decimal Amount)>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}]";

            var serviceItemId = line.ServiceItemId.GetValueOrDefault();
            if (serviceItemId != Guid.Empty)
                AgencyBillingCatalogValidationGuards.EnsureServiceItem(serviceItemId, $"{prefix}.service_item_id", serviceItems);

            if (line.QuantityHours <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.quantity_hours", "Quantity Hours must be greater than zero.");

            if (line.Rate <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.rate", "Rate must be greater than zero.");

            if (line.LineAmount <= 0m)
                throw new NgbArgumentInvalidException($"{prefix}.line_amount", "Line Amount must be greater than zero.");

            var expectedLineAmount = AgencyBillingPostingCommon.RoundScale4(line.QuantityHours * line.Rate);
            if (AgencyBillingPostingCommon.RoundScale4(line.LineAmount) != expectedLineAmount)
            {
                throw new NgbArgumentInvalidException(
                    $"{prefix}.line_amount",
                    $"Line Amount must equal Quantity Hours x Rate ({expectedLineAmount:0.####}).");
            }

            expectedAmount += line.LineAmount;

            if (line.SourceTimesheetId is not { } sourceTimesheetId || sourceTimesheetId == Guid.Empty)
                continue;

            if (!sourceDocuments.TryGetValue(sourceTimesheetId, out var sourceDocument)
                || !string.Equals(sourceDocument.TypeCode, AgencyBillingCodes.Timesheet, StringComparison.OrdinalIgnoreCase))
            {
                throw new NgbArgumentInvalidException($"{prefix}.source_timesheet_id", "Referenced source timesheet was not found.");
            }

            if (sourceDocument.Status != DocumentStatus.Posted)
                throw new NgbArgumentInvalidException($"{prefix}.source_timesheet_id", "Referenced source timesheet must be posted.");

            if (!sourceTimesheets.TryGetValue(sourceTimesheetId, out var sourceTimesheet))
            {
                throw new NgbConfigurationViolationException(
                    $"Timesheet document '{sourceTimesheetId}' is missing its Agency Billing head row.");
            }

            if (sourceTimesheet.ClientId != head.ClientId)
            {
                throw new NgbArgumentInvalidException(
                    $"{prefix}.source_timesheet_id",
                    "Referenced source timesheet must belong to the same client and project as the invoice.");
            }

            if (sourceTimesheet.ProjectId != head.ProjectId)
            {
                throw new NgbArgumentInvalidException(
                    $"{prefix}.source_timesheet_id",
                    "Referenced source timesheet must belong to the same client and project as the invoice.");
            }

            currentUsageByTimesheet.TryGetValue(sourceTimesheetId, out var currentUsage);
            currentUsageByTimesheet[sourceTimesheetId] = (
                AgencyBillingPostingCommon.RoundScale4(currentUsage.Hours + line.QuantityHours),
                AgencyBillingPostingCommon.RoundScale4(currentUsage.Amount + line.LineAmount));
        }

        if (AgencyBillingPostingCommon.RoundScale4(head.Amount) != AgencyBillingPostingCommon.RoundScale4(expectedAmount))
            throw new NgbArgumentInvalidException("amount", "Amount must equal the sum of invoice line amounts.");

        var usedTimesheetIds = currentUsageByTimesheet.Keys.ToArray();
        var timesheetLinesByDocument = await readers.ReadTimesheetLinesAsync(usedTimesheetIds, ct);
        var existingUsageByTimesheet = await invoiceUsageReader.GetPostedInvoiceUsageForTimesheetsAsync(
            usedTimesheetIds,
            ct: ct);

        foreach (var (timesheetId, currentUsage) in currentUsageByTimesheet)
        {
            timesheetLinesByDocument.TryGetValue(timesheetId, out var timesheetLines);
            timesheetLines ??= Array.Empty<AgencyBillingTimesheetLine>();
            var billableHours = AgencyBillingPostingCommon.RoundScale4(timesheetLines.Where(x => x.Billable).Sum(x => x.Hours));
            var billableAmount = AgencyBillingPostingCommon.RoundScale4(timesheetLines.Where(x => x.Billable).Sum(AgencyBillingPostingCommon.ResolveTimesheetLineAmount));
            existingUsageByTimesheet.TryGetValue(timesheetId, out var existingUsage);
            existingUsage ??= new AgencyBillingTimesheetInvoiceUsage(0m, 0m);

            var availableHours = AgencyBillingPostingCommon.RoundScale4(billableHours - existingUsage.InvoicedHours);
            var availableAmount = AgencyBillingPostingCommon.RoundScale4(billableAmount - existingUsage.InvoicedAmount);

            if (currentUsage.Hours > availableHours)
            {
                throw new NgbArgumentInvalidException(
                    "lines",
                    $"Invoice exceeds remaining billable hours for source timesheet '{timesheetId}'.");
            }

            if (currentUsage.Amount > availableAmount)
            {
                throw new NgbArgumentInvalidException(
                    "lines",
                    $"Invoice exceeds remaining billable amount for source timesheet '{timesheetId}'.");
            }
        }
    }

    private async Task ValidateContractAsync(
        Guid contractId,
        AgencyBillingSalesInvoiceHead invoice,
        CancellationToken ct)
    {
        var contractDocument = await documents.GetAsync(contractId, ct);
        if (contractDocument is null
            || !string.Equals(contractDocument.TypeCode, AgencyBillingCodes.ClientContract, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbArgumentInvalidException("contract_id", "Referenced client contract was not found.");
        }

        if (contractDocument.Status != DocumentStatus.Posted)
            throw new NgbArgumentInvalidException("contract_id", "Referenced client contract must be posted.");

        var contract = await readers.ReadClientContractHeadAsync(contractId, ct);
        if (!contract.IsActive)
            throw new NgbArgumentInvalidException("contract_id", "Referenced client contract must be active.");

        if (contract.ClientId != invoice.ClientId)
        {
            throw new NgbArgumentInvalidException(
                "contract_id",
                "Referenced client contract must belong to the same client and project as the invoice.");
        }

        if (contract.ProjectId != invoice.ProjectId)
        {
            throw new NgbArgumentInvalidException(
                "contract_id",
                "Referenced client contract must belong to the same client and project as the invoice.");
        }

        if (invoice.DocumentDateUtc < contract.EffectiveFrom
            || (contract.EffectiveTo is { } effectiveTo && invoice.DocumentDateUtc > effectiveTo))
        {
            throw new NgbArgumentInvalidException("contract_id", "Invoice Date must fall within the client contract effective period.");
        }
    }
}
