using System.Runtime.CompilerServices;
using System.Text.Json;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Core.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;

namespace NGB.Runtime.Reporting.Streaming;

public sealed class TrialBalanceStreamingExecutor(ITrialBalanceAccountSummaryReader reader) : IStreamingReportExecutor
{
    public string ReportCode => AccountingReportCodes.TrialBalance;

    private sealed record Input(
        DateOnly RawFrom,
        DateOnly RawTo,
        DateOnly From,
        DateOnly To,
        DimensionScopeInput[] Scopes,
        ReportExecutionRequestDto Request,
        string Title);

    private sealed record DimensionScopeInput(Guid Id, Guid[] Values);

    public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request)
    {
        var (rawFrom, rawTo, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);
        var scopes = CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request);

        return JsonSerializer.Serialize(new Input(
            rawFrom,
            rawTo,
            from,
            to,
            scopes?.Items.Select(s => new DimensionScopeInput(s.DimensionId, s.ValueIds.ToArray())).ToArray() ?? [],
            request,
            definition.Name));
    }

    public ReportSheetDto Template(string preparedJson)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;

        return new(
        [
            new("account", "Account", "string", Width: 420, IsFrozen: true),
            new("debit_amount", "Debit", "decimal", Width: 140),
            new("credit_amount", "Credit", "decimal", Width: 140)
        ],
        [],
        new(
            Title: input.Title,
            Subtitle: $"{input.RawFrom:yyyy-MM-dd} → {input.RawTo:yyyy-MM-dd}",
            HasRowOutline: true,
            Diagnostics: new Dictionary<string, string> { ["executor"] = "canonical-trial-balance" }));
    }

    public async IAsyncEnumerable<ReportRowWrite> ReadAsync(
        string preparedJson,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        var scopes = input.Scopes.Length == 0
            ? null
            : new DimensionScopeBag(input.Scopes.Select(s => new DimensionScope(s.Id, s.Values)).ToArray());
        var totals = new TrialBalanceReportTotals(0, 0, 0, 0);
        var hasRows = false;
        var ordinal = 0;

        await foreach (var row in TrialBalanceRows.ReadAsync(
            reader.ReadAsync(input.From, input.To, scopes, ct),
            input.Request.Layout?.ShowSubtotals != false,
            ct))
        {
            hasRows = true;
            if (row.RowKind == TrialBalanceReportRowKind.Detail)
            {
                totals = new(
                    totals.OpeningBalance + row.OpeningBalance,
                    totals.DebitAmount + row.DebitAmount,
                    totals.CreditAmount + row.CreditAmount,
                    totals.ClosingBalance + row.ClosingBalance);
            }

            yield return new(
                ordinal++,
                TrialBalanceCanonicalReportExecutor.ToSheetRow(row, input.RawFrom, input.RawTo, input.Request.Filters));
        }

        if (hasRows && input.Request.Layout?.ShowGrandTotals != false)
            yield return new(ordinal, TrialBalanceCanonicalReportExecutor.ToTotalRow(totals));
    }
}
