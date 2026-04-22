using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYearVsPostingConcurrency_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_And_PostAsync_Concurrent_EndMonth_January_NoDeadlock_BothComplete_StatementsAreConsistent()
    {
        await Fixture.ResetDatabaseAsync();

        var endPeriod = new DateOnly(2039, 1, 1);
        var dayUtc = new DateTime(2039, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var retainedEarningsId = await SeedCoaAsync(host);

        // Initial P&L activity (to make FY close write something)
        await PostAsync(host, Guid.CreateVersion7(), dayUtc, debit: "50", credit: "90.1", amount: 100m);
        await PostAsync(host, Guid.CreateVersion7(), dayUtc, debit: "91", credit: "50", amount: 40m);

        var concurrentPostDocId = Guid.CreateVersion7();

        Task closeTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

            await closing.CloseFiscalYearAsync(
                fiscalYearEndPeriod: endPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "test",
                ct: CancellationToken.None);
        });

        Task postTask = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(concurrentPostDocId, dayUtc, chart.Get("50"), chart.Get("90.1"), 25m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        });

        // Assert: no deadlock (completion is enough). Use a hard timeout to fail fast.
        await Task.WhenAll(closeTask, postTask).WaitAsync(TimeSpan.FromSeconds(30));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var logReader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();
            var bsReader = scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

            // CloseFiscalYear is idempotent: it must be completed exactly once.
            var fyLogCount = await CountPostingLogRowsAsync(logReader, DeterministicFiscalYearCloseDocumentId(endPeriod), PostingOperation.CloseFiscalYear);
            fyLogCount.Should().Be(1);

            var postLogCount = await CountPostingLogRowsAsync(logReader, concurrentPostDocId, PostingOperation.Post);
            postLogCount.Should().Be(1);

            // With IncludeNetIncomeInEquity=true, statements must always balance regardless of operation ordering.
            var bs = await bsReader.GetAsync(new BalanceSheetReportRequest
            {
                AsOfPeriod = endPeriod,
                IncludeNetIncomeInEquity = true,
                IncludeZeroAccounts = false
            });

            bs.IsBalanced.Should().BeTrue();
            bs.Difference.Should().Be(0m);
        }
    }

    private static Guid DeterministicFiscalYearCloseDocumentId(DateOnly fiscalYearEndPeriod)
    {
        // See DeterministicGuid usage in PeriodClosingService.
        var s = $"CloseFiscalYear|{fiscalYearEndPeriod:yyyy-MM-dd}";
        return NGB.Tools.Extensions.DeterministicGuid.Create(s);
    }

    private static async Task<Guid> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        var reId = await svc.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        return reId;
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        string debit,
        string credit,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, dateUtc, chart.Get(debit), chart.Get(credit), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task<int> CountPostingLogRowsAsync(
        IPostingStateReader reader,
        Guid documentId,
        PostingOperation operation)
    {
        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-1),
            ToUtc = DateTime.UtcNow.AddHours(1),
            DocumentId = documentId,
            Operation = operation,
            PageSize = 50
        }, CancellationToken.None);

        return page.Records.Count;
    }
}
