using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class GeneralJournalCanonicalReportExecutor(
    IGeneralJournalReportReader reader,
    IDocumentDisplayReader documentDisplayReader,
    IAccountByIdResolver accountByIdResolver)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.general_journal";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (rawFrom, rawTo, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);

        var page = await reader.GetPageAsync(
            new GeneralJournalPageRequest
            {
                FromInclusive = from,
                ToInclusive = to,
                Cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor) ? null : GeneralJournalCursorCodec.Decode(request.Cursor),
                DebitAccountId = CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "debit_account_id"),
                CreditAccountId = CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, request, "credit_account_id"),
                DimensionScopes = CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request),
                IsStorno = CanonicalReportExecutionHelper.GetOptionalBoolFilter(definition, request, "is_storno"),
                PageSize = request.Limit,
                DisablePaging = request.DisablePaging
            },
            ct);

        var documentRefs = await documentDisplayReader.ResolveRefsAsync(page.Lines.Select(x => x.DocumentId).Distinct().ToArray(), ct);
        var accountIds = page.Lines
            .SelectMany(x => new[] { x.DebitAccountId, x.CreditAccountId })
            .Distinct()
            .ToArray();
        var accounts = await accountByIdResolver.GetByIdsAsync(accountIds, ct);

        var rows = page.Lines
            .Select(line => ToDetailRow(line, rawFrom, rawTo, request.Filters, documentRefs, accounts))
            .ToList();

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("period_utc", "Period", "date", Width: 120, IsFrozen: true),
                new ReportSheetColumnDto("document", "Document", "string", Width: 220),
                new ReportSheetColumnDto("debit_account", "Debit", "string", Width: 240),
                new ReportSheetColumnDto("debit_dimensions", "Debit Dimensions", "string", Width: 220),
                new ReportSheetColumnDto("credit_account", "Credit", "string", Width: 240),
                new ReportSheetColumnDto("credit_dimensions", "Credit Dimensions", "string", Width: 220),
                new ReportSheetColumnDto("amount", "Amount", "decimal", Width: 120),
                new ReportSheetColumnDto("is_storno", "Storno", "bool", Width: 80)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"{rawFrom:yyyy-MM-dd} → {rawTo:yyyy-MM-dd}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-general-journal"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: 0,
            limit: request.DisablePaging ? rows.Count : request.Limit,
            total: null,
            hasMore: page.HasMore,
            nextCursor: page.NextCursor is null ? null : GeneralJournalCursorCodec.Encode(page.NextCursor),
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-general-journal"
            });
    }

    private static ReportSheetRowDto ToDetailRow(
        GeneralJournalLine line,
        DateOnly rawFrom,
        DateOnly rawTo,
        IReadOnlyDictionary<string, ReportFilterValueDto>? inheritedFilters,
        IReadOnlyDictionary<Guid, DocumentDisplayRef> documentRefs,
        IReadOnlyDictionary<Guid, Account> accounts)
    {
        var documentRef = documentRefs.TryGetValue(line.DocumentId, out var doc)
            ? doc
            : new DocumentDisplayRef(line.DocumentId, string.Empty, ReportDisplayHelpers.ShortGuid(line.DocumentId));
        var documentDisplay = documentRef.Display;
        var documentAction = string.IsNullOrWhiteSpace(documentRef.TypeCode)
            ? null
            : ReportCellActions.BuildDocumentAction(documentRef.TypeCode, line.DocumentId);
        var debitDimensions = BuildMultilineDimensionSetDisplay(line.DebitDimensions, line.DebitDimensionValueDisplays);
        var creditDimensions = BuildMultilineDimensionSetDisplay(line.CreditDimensions, line.CreditDimensionValueDisplays);
        var debitDisplay = accounts.TryGetValue(line.DebitAccountId, out var debitAccount)
            ? ReportDisplayHelpers.BuildAccountDisplay(debitAccount.Code, debitAccount.Name)
            : line.DebitAccountCode;
        var creditDisplay = accounts.TryGetValue(line.CreditAccountId, out var creditAccount)
            ? ReportDisplayHelpers.BuildAccountDisplay(creditAccount.Code, creditAccount.Name)
            : line.CreditAccountCode;

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.PeriodUtc), line.PeriodUtc.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(documentDisplay), documentDisplay, "string", Action: documentAction),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(debitDisplay), debitDisplay, "string", Action: ReportCellActions.BuildAccountCardAction(line.DebitAccountId, rawFrom, rawTo, inheritedFilters)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(debitDimensions), debitDimensions, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(creditDisplay), creditDisplay, "string", Action: ReportCellActions.BuildAccountCardAction(line.CreditAccountId, rawFrom, rawTo, inheritedFilters)),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(creditDimensions), creditDimensions, "string"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.Amount), line.Amount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.IsStorno), line.IsStorno ? "Yes" : "No", "bool")
            ]);
    }

    private static string BuildMultilineDimensionSetDisplay(
        DimensionBag bag,
        IReadOnlyDictionary<Guid, string>? displays)
    {
        var values = bag.ToDimensionDisplayValues(displays);
        return values.Count == 0 ? string.Empty : string.Join(Environment.NewLine, values);
    }
}
