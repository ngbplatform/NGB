using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.AccountCard;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Internal;

namespace NGB.Runtime.Reporting.Canonical;

public sealed class AccountCardCanonicalReportExecutor(
    IAccountCardEffectivePagedReportReader reader,
    IDocumentDisplayReader documentDisplayReader,
    IAccountByIdResolver accountByIdResolver)
    : IReportSpecializedPlanExecutor
{
    public string ReportCode => "accounting.account_card";

    public async Task<ReportDataPage> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var (rawFrom, rawTo, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);
        var accountId = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "account_id");

        var scopes = CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request);
        var cursor = request.DisablePaging || string.IsNullOrWhiteSpace(request.Cursor) ? null : AccountCardCursorCodec.Decode(request.Cursor.Trim());
        var page = await reader.GetPageAsync(new AccountCardReportPageRequest
        {
            AccountId = accountId,
            FromInclusive = from,
            ToInclusive = to,
            DimensionScopes = scopes,
            Cursor = cursor,
            PageSize = request.Limit,
            DisablePaging = request.DisablePaging
        }, ct);

        var documentRefs = await documentDisplayReader.ResolveRefsAsync(page.Lines.Select(x => x.DocumentId).Distinct().ToArray(), ct);
        var counterAccounts = await accountByIdResolver.GetByIdsAsync(page.Lines.Select(x => x.CounterAccountId).Distinct().ToArray(), ct);
        var selectedAccount = await accountByIdResolver.GetByIdAsync(accountId, ct);
        var accountDisplay = selectedAccount is null
            ? page.AccountCode
            : ReportDisplayHelpers.BuildAccountDisplay(selectedAccount.Code, selectedAccount.Name);

        var rows = page.Lines.Select(line => ToDetailRow(line, rawFrom, rawTo, request.Filters, documentRefs, counterAccounts)).ToList();

        var sheet = new ReportSheetDto(
            Columns:
            [
                new ReportSheetColumnDto("period_utc", "Period", "date", Width: 140, IsFrozen: true),
                new ReportSheetColumnDto("counter_account", "Counter", "string", Width: 220),
                new ReportSheetColumnDto("document", "Document", "string", Width: 220),
                new ReportSheetColumnDto("debit_amount", "Debit", "decimal", Width: 120),
                new ReportSheetColumnDto("credit_amount", "Credit", "decimal", Width: 120),
                new ReportSheetColumnDto("delta", "Delta", "decimal", Width: 120),
                new ReportSheetColumnDto("running_balance", "Running", "decimal", Width: 120)
            ],
            Rows: rows,
            Meta: new ReportSheetMetaDto(
                Title: definition.Name,
                Subtitle: $"{accountDisplay} · {rawFrom:yyyy-MM-dd} → {rawTo:yyyy-MM-dd} · Opening {page.OpeningBalance:0.##}",
                Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["executor"] = "canonical-account-card"
                }));

        return CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet: sheet,
            offset: 0,
            limit: request.DisablePaging ? rows.Count : request.Limit,
            total: null,
            hasMore: page.HasMore,
            nextCursor: page.NextCursor is null ? null : AccountCardCursorCodec.Encode(page.NextCursor),
            diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["executor"] = "canonical-account-card"
            });
    }

    private static ReportSheetRowDto ToDetailRow(
        AccountCardReportLine line,
        DateOnly rawFrom,
        DateOnly rawTo,
        IReadOnlyDictionary<string, ReportFilterValueDto>? inheritedFilters,
        IReadOnlyDictionary<Guid, DocumentDisplayRef> documentRefs,
        IReadOnlyDictionary<Guid, Account> counterAccounts)
    {
        var documentRef = documentRefs.TryGetValue(line.DocumentId, out var docRef)
            ? docRef
            : new DocumentDisplayRef(line.DocumentId, string.Empty, ReportDisplayHelpers.ShortGuid(line.DocumentId));
        var counterDisplay = counterAccounts.TryGetValue(line.CounterAccountId, out var account)
            ? ReportDisplayHelpers.BuildAccountDisplay(account.Code, account.Name)
            : line.CounterAccountCode;
        var period = DateOnly.FromDateTime(line.PeriodUtc);
        var documentAction = string.IsNullOrWhiteSpace(documentRef.TypeCode)
            ? null
            : ReportCellActions.BuildDocumentAction(documentRef.TypeCode, line.DocumentId);
        var counterAccountAction = ReportCellActions.BuildAccountCardAction(line.CounterAccountId, rawFrom, rawTo, inheritedFilters);

        return new ReportSheetRowDto(
            ReportRowKind.Detail,
            Cells:
            [
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(period), period.ToString("yyyy-MM-dd"), "date"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(counterDisplay), counterDisplay, "string", Action: counterAccountAction),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(documentRef.Display), documentRef.Display, "string", Action: documentAction),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.DebitAmount), line.DebitAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.CreditAmount), line.CreditAmount.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.Delta), line.Delta.ToString("0.##"), "decimal"),
                new ReportCellDto(CanonicalReportExecutionHelper.JsonValue(line.RunningBalance), line.RunningBalance.ToString("0.##"), "decimal")
            ]);
    }
}
