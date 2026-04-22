using NGB.AgencyBilling.Derivations;
using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.Runtime.Derivations.Exceptions;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.Core.Documents;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Derivations;

public sealed class GenerateInvoiceDraftFromTimesheetDerivationHandler(
    IAgencyBillingDocumentReaders documentReaders,
    IAgencyBillingInvoiceDraftDerivationReader derivationReader,
    IDocumentTypeRegistry documentTypes,
    IDocumentWriter writer,
    IDocumentPartsWriter partsWriter,
    IDocumentRepository documents)
    : IDocumentDerivationHandler
{
    public async Task ApplyAsync(DocumentDerivationContext ctx, CancellationToken ct = default)
    {
        if (!string.Equals(ctx.SourceDocument.TypeCode, AgencyBillingCodes.Timesheet, StringComparison.OrdinalIgnoreCase))
        {
            throw new NgbConfigurationViolationException(
                message: "Agency Billing invoice draft derivation expects a timesheet source document.",
                context: new Dictionary<string, object?>
                {
                    ["sourceTypeCode"] = ctx.SourceDocument.TypeCode,
                    ["targetTypeCode"] = ctx.TargetDraft.TypeCode
                });
        }

        if (ctx.SourceDocument.Status != DocumentStatus.Posted)
        {
            throw new DocumentWorkflowStateMismatchException(
                operation: "AgencyBilling.GenerateInvoiceDraft",
                documentId: ctx.SourceDocument.Id,
                expectedState: nameof(DocumentStatus.Posted),
                actualState: ctx.SourceDocument.Status.ToString());
        }

        var timesheetHead = await documentReaders.ReadTimesheetHeadAsync(ctx.SourceDocument.Id, ct);
        var timesheetLines = await documentReaders.ReadTimesheetLinesAsync(ctx.SourceDocument.Id, ct);

        if (await derivationReader.HasExistingInvoiceForTimesheetAsync(ctx.SourceDocument.Id, ct))
            throw new AgencyBillingInvoiceDraftAlreadyExistsException(ctx.SourceDocument.Id);

        var defaults = await derivationReader.ResolveDefaultsAsync(
            timesheetHead.ClientId,
            timesheetHead.ProjectId,
            timesheetHead.WorkDate,
            ct);

        if (defaults is null)
        {
            throw new AgencyBillingInvoiceDraftContractNotFoundException(
                ctx.SourceDocument.Id,
                timesheetHead.ClientId,
                timesheetHead.ProjectId,
                timesheetHead.WorkDate);
        }

        var lines = BuildInvoiceLines(timesheetHead, timesheetLines);
        if (lines.Count == 0)
            throw new AgencyBillingInvoiceDraftNoBillableTimeException(ctx.SourceDocument.Id);

        var invoiceDate = timesheetHead.DocumentDateUtc;
        var dueDate = invoiceDate.AddDays(defaults.DueDays);
        var totalAmount = lines.Sum(line => line.LineAmount);

        var salesInvoiceMeta = documentTypes.TryGet(AgencyBillingCodes.SalesInvoice)
            ?? throw new NgbConfigurationViolationException(
                $"Document type '{AgencyBillingCodes.SalesInvoice}' is not registered.");

        var head = salesInvoiceMeta.CreateHeadDescriptor();
        var partsTable = salesInvoiceMeta.GetRequiredPartTable("lines");

        await writer.UpsertHeadAsync(
            head,
            ctx.TargetDraft.Id,
            BuildHeadValues(timesheetHead, defaults, invoiceDate, dueDate, totalAmount),
            ct);

        await partsWriter.ReplacePartsAsync(
            [partsTable],
            ctx.TargetDraft.Id,
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase)
            {
                [partsTable.TableName] = lines
                    .Select(static line => line.ToRow())
                    .ToArray()
            },
            ct);

        await documents.UpdateDraftHeaderAsync(
            ctx.TargetDraft.Id,
            ctx.TargetDraft.Number,
            AgencyBillingPostingCommon.ToOccurredAtUtc(invoiceDate),
            DateTime.UtcNow,
            ct);
    }

    private static IReadOnlyList<DocumentHeadValue> BuildHeadValues(
        AgencyBillingTimesheetHead source,
        AgencyBillingInvoiceDraftDefaults defaults,
        DateOnly invoiceDate,
        DateOnly dueDate,
        decimal totalAmount)
    {
        var values = new List<DocumentHeadValue>
        {
            new("document_date_utc", ColumnType.Date, invoiceDate),
            new("due_date", ColumnType.Date, dueDate),
            new("client_id", ColumnType.Guid, source.ClientId),
            new("project_id", ColumnType.Guid, source.ProjectId),
            new("contract_id", ColumnType.Guid, defaults.ContractId),
            new("currency_code", ColumnType.String, defaults.CurrencyCode),
            new("amount", ColumnType.Decimal, AgencyBillingPostingCommon.RoundScale4(totalAmount))
        };

        if (!string.IsNullOrWhiteSpace(defaults.InvoiceMemo))
            values.Add(new("memo", ColumnType.String, defaults.InvoiceMemo));

        return values;
    }

    private static IReadOnlyList<DerivedInvoiceLine> BuildInvoiceLines(
        AgencyBillingTimesheetHead timesheetHead,
        IReadOnlyList<AgencyBillingTimesheetLine> sourceLines)
    {
        var lines = new List<DerivedInvoiceLine>();

        foreach (var line in sourceLines.OrderBy(x => x.Ordinal))
        {
            if (!line.Billable)
                continue;

            var quantityHours = AgencyBillingPostingCommon.RoundScale4(line.Hours);
            var lineAmount = AgencyBillingPostingCommon.RoundScale4(AgencyBillingPostingCommon.ResolveTimesheetLineAmount(line));

            if (quantityHours <= 0m || lineAmount <= 0m)
                continue;

            var rate = line.BillingRate.HasValue
                ? AgencyBillingPostingCommon.RoundScale4(line.BillingRate.Value)
                : quantityHours == 0m
                    ? 0m
                    : AgencyBillingPostingCommon.RoundScale4(lineAmount / quantityHours);

            var description = string.IsNullOrWhiteSpace(line.Description)
                ? $"Billable time {timesheetHead.WorkDate:M/d/yyyy}"
                : line.Description.Trim();

            lines.Add(new DerivedInvoiceLine(
                Ordinal: lines.Count + 1,
                ServiceItemId: line.ServiceItemId,
                SourceTimesheetId: timesheetHead.DocumentId,
                Description: description,
                QuantityHours: quantityHours,
                Rate: rate,
                LineAmount: lineAmount));
        }

        return lines;
    }

    private sealed record DerivedInvoiceLine(
        int Ordinal,
        Guid? ServiceItemId,
        Guid SourceTimesheetId,
        string Description,
        decimal QuantityHours,
        decimal Rate,
        decimal LineAmount)
    {
        public IReadOnlyDictionary<string, object?> ToRow()
            => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = Ordinal,
                ["service_item_id"] = ServiceItemId,
                ["source_timesheet_id"] = SourceTimesheetId,
                ["description"] = Description,
                ["quantity_hours"] = QuantityHours,
                ["rate"] = Rate,
                ["line_amount"] = LineAmount
            };
    }
}
