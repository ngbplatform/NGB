using NGB.Accounting.Accounts;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NGB.Accounting.Reports;
using NGB.Accounting.Reports.AccountCard;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Streaming;

public sealed class AccountCardStreamingExecutor(
    IAccountCardEffectiveStreamReader reader,
    IAccountCardEffectivePageReader openingReader,
    IDocumentDisplayReader documents,
    IAccountByIdResolver accounts) : IStreamingReportExecutor
{
    public string ReportCode => AccountingReportCodes.AccountCard;
    private sealed record Input(ReportDefinitionDto Definition, ReportExecutionRequestDto Request);
    private ReportSheetDto? _template;

    public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request)
    {
        _ = CanonicalReportExecutionHelper.GetRequiredDateRange(definition, request);
        _ = CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, request, "account_id");
        return JsonSerializer.Serialize(new Input(definition, request));
    }

    public ReportSheetDto Template(string preparedJson)
        => _template ?? throw new NgbInvariantViolationException("The account report stream has not been initialized.");

    public async IAsyncEnumerable<ReportRowWrite> ReadAsync(string preparedJson, [EnumeratorCancellation] CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<Input>(preparedJson)!;
        var (_, _, from, to) = CanonicalReportExecutionHelper.GetRequiredDateRange(input.Definition, input.Request);
        var accountId = CanonicalReportExecutionHelper.GetRequiredGuidFilter(input.Definition, input.Request, "account_id");
        var scopes = CanonicalReportExecutionHelper.BuildDimensionScopes(input.Definition, input.Request);
        var opening = await openingReader.GetOpeningBalanceAsync(accountId, from, scopes, ct);
        var totalDebit = 0m;
        var totalCredit = 0m;
        var closing = opening;
        var running = opening;
        var ordinal = 0;

        AccountCardReportPage Page(IReadOnlyList<AccountCardReportLine> lines, bool hasMore) => new()
        {
            AccountId = accountId, FromInclusive = from, ToInclusive = to,
            OpeningBalance = opening, TotalDebit = totalDebit, TotalCredit = totalCredit,
            ClosingBalance = closing, Lines = lines, HasMore = hasMore
        };

        _template = await AccountCardCanonicalReportExecutor.RenderAsync(input.Definition, input.Request, Page([], true), documents, accounts, ct);

        await foreach (var batch in reader.ReadAsync(new AccountActivityQuery(accountId, from, to, scopes), ct))
        {
            var lines = new List<AccountCardReportLine>(batch.Count);
            foreach (var line in batch)
            {
                running += line.Delta;
                totalDebit += line.DebitAmount;
                totalCredit += line.CreditAmount;
                lines.Add(new()
                {
                    EntryId = line.EntryId,
                    Delta = line.Delta,
                    PeriodUtc = line.PeriodUtc, DocumentId = line.DocumentId,
                    AccountId = line.AccountId, AccountCode = line.AccountCode,
                    CounterAccountId = line.CounterAccountId, CounterAccountCode = line.CounterAccountCode,
                    DimensionSetId = line.DimensionSetId, Dimensions = line.Dimensions,
                    DimensionValueDisplays = line.DimensionValueDisplays,
                    DebitAmount = line.DebitAmount, CreditAmount = line.CreditAmount, RunningBalance = running
                });
            }

            var sheet = await AccountCardCanonicalReportExecutor.RenderAsync(input.Definition, input.Request, Page(lines, true), documents, accounts, ct);

            foreach (var row in sheet.Rows)
            {
                yield return new(ordinal++, row);
            }
        }
    }
}
