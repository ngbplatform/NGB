using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class GeneralLedgerAggregatedCanonicalReportExecutor(
    IGeneralLedgerAggregatedPagedReportReader reader,
    IDocumentDisplayReader documentDisplayReader,
    IAccountByIdResolver accountByIdResolver)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.general_ledger_aggregated";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (rawFrom, rawTo, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);
        var accountId = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "account_id");

        var report = await reader.GetPageAsync(
            new GeneralLedgerAggregatedReportPageRequest
            {
                AccountId = accountId,
                FromInclusive = from,
                ToInclusive = to,
                DimensionScopes = CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request),
                PageSize = request.Limit,
                Cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor)
                    ? null
                    : GeneralLedgerAggregatedCursorCodec.Decode(request.Cursor),
                DisablePaging = request.DisablePaging
            },
            ct);

        var documentRefs = await documentDisplayReader.ResolveRefsAsync(report.Lines.Select(x => x.DocumentId).Distinct().ToArray(), ct);
        var counterAccounts = await accountByIdResolver.GetByIdsAsync(report.Lines.Select(x => x.CounterAccountId).Distinct().ToArray(), ct);
        var selectedAccount = await accountByIdResolver.GetByIdAsync(accountId, ct);
        var accountDisplay = selectedAccount is null
            ? report.AccountCode
            : ReportDisplayHelpers.BuildAccountDisplay(selectedAccount.Code, selectedAccount.Name);

        var rows = report.Lines
            .Select(line => ToDetailRow(line, rawFrom, rawTo, request.Filters, documentRefs, counterAccounts))
            .ToList();

        if (request.Layout?.ShowGrandTotals != false && !report.HasMore)
            rows.Add(ToTotalRow(report));

        var subtitle = $"{accountDisplay} · {from:yyyy-MM-dd} → {to:yyyy-MM-dd} · Opening {report.OpeningBalance:0.##} · Closing {report.ClosingBalance:0.##}";

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("period_utc", "Period", "date", Width: 140, IsFrozen: true),
                new ReportSheetColumnDto("counter_account", "Counter", "string", Width: 220),
                new ReportSheetColumnDto("dimensions", "Dimensions", "string", Width: 220),
                new ReportSheetColumnDto("document", "Document", "string", Width: 220),
                new ReportSheetColumnDto("debit_amount", "Debit", "decimal", Width: 120),
                new ReportSheetColumnDto("credit_amount", "Credit", "decimal", Width: 120),
                new ReportSheetColumnDto("delta", "Delta", "decimal", Width: 120),
                new ReportSheetColumnDto("running_balance", "Running", "decimal", Width: 120)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: subtitle,
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-general-ledger-aggregated"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: 0,
            limit: request.DisablePaging ? rows.Count : request.Limit,
            total: null,
            hasMore: report.HasMore,
            nextCursor: report.NextCursor is null ? null : GeneralLedgerAggregatedCursorCodec.Encode(report.NextCursor),
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-general-ledger-aggregated"
            });
    }

    private static ReportSheetRowDto ToDetailRow(
        GeneralLedgerAggregatedReportLine line,
        DateOnly rawFrom,
        DateOnly rawTo,
        IReadOnlyDictionary<string, ReportFilterValueDto>? inheritedFilters,
        IReadOnlyDictionary<Guid, DocumentDisplayRef> documentRefs,
        IReadOnlyDictionary<Guid, Account> counterAccounts)
    {
        var documentRef = documentRefs.TryGetValue(line.DocumentId, out var doc)
            ? doc
            : new DocumentDisplayRef(line.DocumentId, string.Empty, ReportDisplayHelpers.ShortGuid(line.DocumentId));
        var documentDisplay = documentRef.Display;
        var documentAction = string.IsNullOrWhiteSpace(documentRef.TypeCode)
            ? null
            : ReportCellActions.BuildDocumentAction(documentRef.TypeCode, line.DocumentId);
        var counterDisplay = counterAccounts.TryGetValue(line.CounterAccountId, out var account)
            ? ReportDisplayHelpers.BuildAccountDisplay(account.Code, account.Name)
            : line.CounterAccountCode;
        var dimensionValues = line.Dimensions.ToDimensionDisplayValues(line.DimensionValueDisplays);
        var dimensionsDisplay = dimensionValues.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, dimensionValues);
        var delta = line.DebitAmount - line.CreditAmount;
        var period = DateOnly.FromDateTime(line.PeriodUtc);

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(period), period.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(counterDisplay), counterDisplay, "string", Action: ReportCellActions.BuildAccountCardAction(line.CounterAccountId, rawFrom, rawTo, inheritedFilters)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(dimensionsDisplay), dimensionsDisplay, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(documentDisplay), documentDisplay, "string", Action: documentAction),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.DebitAmount), line.DebitAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.CreditAmount), line.CreditAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(delta), delta.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.RunningBalance), line.RunningBalance.ToString("0.##"), "decimal")
            ]);
    }

    private static ReportSheetRowDto ToTotalRow(GeneralLedgerAggregatedReportPage report)
    {
        var delta = report.TotalDebit - report.TotalCredit;
        return new ReportSheetRowDto(
            ReportRowKind.Total,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue("Total"), "Total", "string", ColSpan: 4, SemanticRole: "label"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.TotalDebit), report.TotalDebit.ToString("0.##"), "decimal", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.TotalCredit), report.TotalCredit.ToString("0.##"), "decimal", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(delta), delta.ToString("0.##"), "decimal", SemanticRole: "total"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(report.ClosingBalance), report.ClosingBalance.ToString("0.##"), "decimal", SemanticRole: "total")
            ],
            SemanticRole: "grand_total");
    }
}
