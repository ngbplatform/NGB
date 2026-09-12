using System.Runtime.CompilerServices;
using System.Text.Json;
using NGB.Accounting.Reports.AccountingConsistency;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Core.Reporting;
using NGB.Persistence.Checkers;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;

namespace NGB.Runtime.Reporting.Runs;

public sealed class AccountingConsistencyStreamingExecutor(
    IAccountingIntegrityDiagnostics integrity,
    IAccountingConsistencyStreamReader reader,
    IDimensionSetReader dimensions,
    IDimensionValueEnrichmentReader values)
    : IStreamingReportExecutor
{
    public string ReportCode => AccountingReportCodes.Consistency;

    private sealed record Input(
        string Title,
        DateOnly RawPeriod,
        DateOnly? RawPrevious,
        DateOnly Period,
        DateOnly? Previous,
        bool Totals);
    
    public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request)
    {
        var raw = CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(definition, request, "period_utc");
        var previous = CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(definition, request, "previous_period_utc");

        return JsonSerializer.Serialize(new Input(
            definition.Name,
            raw,
            previous,
            new(raw.Year, raw.Month, 1),
            previous is { } p ? new(p.Year, p.Month, 1) : null,
            request.Layout?.ShowGrandTotals != false));
    }
    public ReportSheetDto Template(string preparedJson)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;

        return new(
            [
                new("kind", "Kind", "string", Width: 160, IsFrozen: true),
                new("period", "Month", "date", Width: 120),
                new("previous_period", "Previous month", "date", Width: 120),
                new("account_code", "Account", "string", Width: 120),
                new("dimension_set", "Dimensions", "string", Width: 220),
                new("message", "Message", "string", Width: 420)
            ],
            [],
            new(
                Title: input.Title,
                Subtitle: input.RawPrevious is { } previous
                    ? $"{input.RawPeriod:yyyy-MM-dd} · previous {previous:yyyy-MM-dd}"
                    : $"{input.RawPeriod:yyyy-MM-dd}",
                Diagnostics: new Dictionary<string, string> { ["executor"] = "canonical-accounting-consistency" }));
    }
    public async IAsyncEnumerable<ReportRowWrite> ReadAsync(
        string preparedJson,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        var diff = await integrity.GetTurnoversVsRegisterDiffCountAsync(input.Period, ct);
        var hasBalances = await reader.HasBalancesAsync(input.Period, ct);
        var ordinal = 0;
        long mismatch = 0, missing = 0, chain = 0;

        if (diff != 0)
        {
            var issue = new AccountingConsistencyIssue { Kind = AccountingConsistencyIssueKind.TurnoversVsRegisterMismatch, Period = input.Period,
                Message = $"Stored turnovers differ from register aggregation for period {input.Period:yyyy-MM-dd}. Diff rows: {diff}." };

            yield return new(
                ordinal++,
                AccountingConsistencyCanonicalReportExecutor
                    .ToDetailRow(issue, new Dictionary<Guid, DimensionBag>(), new Dictionary<DimensionValueKey, string>()));
        }

        await foreach (var batch in reader.ReadAsync(input.Period, input.Previous, ct))
        {
            var issues = new List<AccountingConsistencyIssue>();
            foreach (var row in batch)
            {
                var expected = row.OpeningBalance + row.DebitAmount - row.CreditAmount;
                if (row.HasCurrentBalanceRow && expected != row.ClosingBalance)
                {
                    mismatch++;
                    issues.Add(Issue(
                        AccountingConsistencyIssueKind.BalanceVsTurnoverMismatch,
                        $"Balance mismatch. Expected closing={expected}, actual closing={row.ClosingBalance}, opening={row.OpeningBalance}, debit={row.DebitAmount}, credit={row.CreditAmount}."));
                }

                if (hasBalances && row is { HasTurnoverRow: true, HasCurrentBalanceRow: false })
                {
                    missing++;
                    issues.Add(Issue(
                        AccountingConsistencyIssueKind.MissingKey,
                        "Turnover exists but balance row is missing for this key."));
                }
                
                var opening = row.HasCurrentBalanceRow ? row.OpeningBalance : 0m;
                var previous = row.HasPreviousBalanceRow ? row.PreviousClosingBalance : 0m;

                if (hasBalances && input.Previous is not null && opening != previous)
                {
                    chain++;
                    issues.Add(Issue(
                        AccountingConsistencyIssueKind.ClosedPeriodChainBroken,
                        $"Closed chain mismatch. Previous closing={previous}, current opening={opening}.",
                        input.Previous));
                }
                AccountingConsistencyIssue Issue(
                    AccountingConsistencyIssueKind kind,
                    string message,
                    DateOnly? prior = null)
                    => new()
                    { 
                        Kind = kind,
                        Period = input.Period,
                        PreviousPeriod = prior,
                        AccountId = row.AccountId,
                        AccountCode = row.AccountCode,
                        DimensionSetId = row.DimensionSetId,
                        Message = message 
                    };
            }

            var bags = await dimensions.GetBagsByIdsAsync(issues
                    .Select(i => i.DimensionSetId!.Value)
                    .Distinct()
                    .ToArray(),
                ct);

            var enriched = await values.ResolveAsync(bags.Values.CollectValueKeys(), ct);
            
            foreach (var issue in issues)
            {
                yield return new(
                    ordinal++,
                    AccountingConsistencyCanonicalReportExecutor.ToDetailRow(issue, bags, enriched));
            }
        }

        if (!input.Totals)
            yield break;

        var count = ordinal;
        foreach (var (label, value) in new[] 
            {
                ("Turnovers vs register", diff),
                ("Balance vs turnover", mismatch),
                ("Balance chain", chain),
                ("Missing keys", missing),
                ("Issue count", (long)count)
            })
        {
            yield return new(ordinal++, AccountingConsistencyCanonicalReportExecutor.TotalRow(label, value));
        }
    }
}
