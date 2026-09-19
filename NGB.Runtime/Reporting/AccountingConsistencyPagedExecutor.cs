using System.Text.Json;
using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Checkers;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Streaming;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed class AccountingConsistencyPagedExecutor(
    IAccountingConsistencyPageReader reader,
    IAccountingIntegrityDiagnostics integrity,
    IDimensionSetReader dimensions,
    IDimensionValueEnrichmentReader values,
    IEnumerable<IStreamingReportExecutor> streaming)
{
    public async Task<ReportExecutionResult> ExecuteAsync(
        ReportDefinitionDto definition,
        ReportExecutionRequestDto request,
        CancellationToken ct)
    {
        var raw = CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(definition, request, "period_utc");
        var prior = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "previous_period_utc");
        var period = new DateOnly(raw.Year, raw.Month, 1);
        DateOnly? previous = prior is { } p ? new(p.Year, p.Month, 1) : null;
        AccountingConsistencyKey? after = null;

        if (!string.IsNullOrEmpty(request.Cursor))
        {
            try
            {
                after = JsonSerializer.Deserialize<AccountingConsistencyKey>(Convert.FromBase64String(request.Cursor));
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                throw new NgbArgumentInvalidException("cursor", "Invalid consistency cursor.");
            }

            if (after?.Code is null)
                throw new NgbArgumentInvalidException("cursor", "Invalid consistency cursor.");
        }

        var limit = Math.Clamp(request.Limit / 3, 1, 166);
        var page = await reader.ReadPageAsync(period, previous, after, limit, ct);
        var issues = new List<AccountingConsistencyIssue>();
        
        // This global diagnostic is represented once. Totals are evaluated only at the end.
        var diff = after is null || !page.HasMore ? await integrity.GetTurnoversVsRegisterDiffCountAsync(period, ct) : 0;
        if (after is null && diff != 0)
        {
            issues.Add(new() { 
                Kind = AccountingConsistencyIssueKind.TurnoversVsRegisterMismatch,
                Period = period,
                Message = $"Stored turnovers differ from register aggregation for period {period:yyyy-MM-dd}. Diff rows: {diff}." });
        }

        foreach (var row in page.Rows)
        {
            var expected = row.OpeningBalance + row.DebitAmount - row.CreditAmount;
            if (row.HasCurrentBalanceRow && expected != row.ClosingBalance)
            {
                issues.Add(Issue(
                    AccountingConsistencyIssueKind.BalanceVsTurnoverMismatch,
                    $"Balance mismatch. Expected closing={expected}, actual closing={row.ClosingBalance}, opening={row.OpeningBalance}, debit={row.DebitAmount}, credit={row.CreditAmount}."));
            }

            if (page.HasBalances && row is { HasTurnoverRow: true, HasCurrentBalanceRow: false })
            {
                issues.Add(Issue(
                    AccountingConsistencyIssueKind.MissingKey,
                    "Turnover exists but balance row is missing for this key."));
            }

            if (page.HasBalances && previous is not null && row.OpeningBalance != row.PreviousClosingBalance)
            {
                issues.Add(Issue(
                    AccountingConsistencyIssueKind.ClosedPeriodChainBroken,
                    $"Closed chain mismatch. Previous closing={row.PreviousClosingBalance}, current opening={row.OpeningBalance}.",
                    previous));
            }

            AccountingConsistencyIssue Issue(AccountingConsistencyIssueKind kind, string message, DateOnly? priorPeriod = null)
                => new() { 
                    Kind = kind,
                    Period = period,
                    PreviousPeriod = priorPeriod,
                    AccountId = row.AccountId,
                    AccountCode = row.AccountCode,
                    DimensionSetId = row.DimensionSetId,
                    Message = message
                };
        }

        var ids = issues
            .Where(i => i.DimensionSetId.HasValue)
            .Select(i => i.DimensionSetId!.Value)
            .Distinct()
            .ToArray();

        var bags = ids.Length == 0
            ? new Dictionary<Guid, DimensionBag>()
            : await dimensions.GetBagsByIdsAsync(ids, ct);

        var keys = bags.Values.CollectValueKeys();

        var enriched = keys.Count == 0
            ? new Dictionary<DimensionValueKey, string>()
            : await values.ResolveAsync(keys, ct);

        var rows = issues
            .Select(i => AccountingConsistencyCanonicalReportExecutor.ToDetailRow(i, bags, enriched))
            .ToList();

        if (!page.HasMore && request.Layout?.ShowGrandTotals != false)
        {
            var counts = await reader.CountAsync(period, previous, ct);
            foreach (var (label, count) in new[] 
                {
                    ("Turnovers vs register", diff),
                    ("Balance vs turnover", counts.Mismatch),
                    ("Balance chain", counts.Chain),
                    ("Missing keys", counts.Missing),
                    ("Issue count", counts.Mismatch + counts.Missing + counts.Chain + (diff != 0 ? 1 : 0))
                })
            {
                rows.Add(AccountingConsistencyCanonicalReportExecutor.TotalRow(label, count));
            }
        }

        var executor = streaming.Single(e => e.ReportCode == definition.ReportCode);
        var template = executor.Template(executor.Prepare(definition, request));

        return new(
            template with { Rows = rows },
            0,
            limit * 3,
            null,
            page.HasMore,
            page.Next is null
                ? null
                : Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(page.Next)),
            new Dictionary<string, string>
                {
                    ["paging"] = "query",
                    ["executor"] = "canonical-accounting-consistency"
                });
    }
}
