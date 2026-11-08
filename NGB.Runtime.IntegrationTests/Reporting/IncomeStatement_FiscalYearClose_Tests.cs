using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.IncomeStatement;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PlatformPostgresCollection.Name)]
public sealed class IncomeStatement_FiscalYearClose_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task FiscalClose_ExcludesOnlyClosingActivity_WithinRequestedMonthsAndDimensions()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (_, revenueId, expenseId) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var rules = new[] { new AccountDimensionRuleRequest("profit_scope", IsRequired: true) };
        await management.UpdateAsync(new UpdateAccountRequest(revenueId, DimensionRules: rules));
        await management.UpdateAsync(new UpdateAccountRequest(expenseId, DimensionRules: rules));
        var retainedId = await management.CreateAsync(new CreateAccountRequest(
            "300", "Retained earnings", AccountType.Equity, StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow));
        var chart = await scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>().GetAsync();
        var dimensionId = chart.Get("90.1").DimensionRules.Single().DimensionId;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        async Task PostAsync(int month, Guid value, decimal income, decimal expense)
        {
            var documentId = Guid.NewGuid();
            var date = new DateTime(2026, month, 15, 0, 0, 0, DateTimeKind.Utc);
            var dimensions = new DimensionBag([new DimensionValue(dimensionId, value)]);
            await scope.ServiceProvider.GetRequiredService<PostingEngine>().PostAsync(PostingOperation.Post, (context, _) =>
            {
                context.Post(documentId, date, chart.Get("50"), chart.Get("90.1"), income, creditDimensions: dimensions);
                context.Post(documentId, date, chart.Get("91"), chart.Get("50"), expense, debitDimensions: dimensions);
                return Task.CompletedTask;
            });
        }

        await PostAsync(1, first, 100m, 140m);
        await PostAsync(1, second, 200m, 10m);
        await PostAsync(2, first, 20m, 5m);
        var january = new DateOnly(2026, 1, 1);
        var february = january.AddMonths(1);
        var periods = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await periods.CloseMonthAsync(january, "test");
        await periods.CloseFiscalYearAsync(february, retainedId, "test");

        async Task AssertProfitAsync(DateOnly from, DateOnly to, DimensionScopeBag? scopes, decimal expected)
        {
            var report = await scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>().GetAsync(
                new IncomeStatementReportRequest { FromInclusive = from, ToInclusive = to, DimensionScopes = scopes });
            report.NetIncome.Should().Be(expected);

            var paged = scope.ServiceProvider.GetRequiredService<IAccountingSummaryPageReader>();
            var query = new AccountingSummaryQuery(AccountingSummaryKind.IncomeStatement, from, to, scopes);
            var groups = await paged.ReadGroupsAsync(query, CancellationToken.None);
            groups.Sum(row => row.Credit - row.Debit).Should().Be(expected);
            foreach (var group in groups)
            {
                var page = await paged.ReadAccountsAsync(query, group.Group, null, 100, CancellationToken.None);
                page.Rows.Sum(row => row.Credit - row.Debit).Should().Be(group.Credit - group.Debit);
            }

            var streaming = (IIncomeStatementAccountReader)scope.ServiceProvider.GetRequiredService<IAccountingStatementAccountReader>();
            var streamedProfit = 0m;
            await foreach (var account in streaming.ReadIncomeStatementAsync(from, to, scopes, CancellationToken.None))
            {
                if (account.Section >= StatementSection.Income)
                    streamedProfit += account.Credit - account.Debit;
            }

            streamedProfit.Should().Be(expected);
        }

        var selected = new DimensionScopeBag([new DimensionScope(dimensionId, [first])]);
        await AssertProfitAsync(january, february, null, 165m);
        await AssertProfitAsync(january, february, selected, -25m);
        await AssertProfitAsync(january, january, selected, -40m);
        await AssertProfitAsync(february, february, selected, 15m);
        await AssertProfitAsync(january, february, new DimensionScopeBag([new DimensionScope(dimensionId, [Guid.NewGuid()])]), 0m);
    }

    [Theory]
    [InlineData("report")]
    [InlineData("paged")]
    [InlineData("export")]
    public async Task FiscalCloseAndReopen_PreserveProfit_InEveryReportPath(string path)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var retainedId = await scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>()
            .CreateAsync(new CreateAccountRequest("300", "Retained earnings", AccountType.Equity,
                StatementSection.Equity, NegativeBalancePolicy: NegativeBalancePolicy.Allow));
        await ReportingTestHelpers.PostAsync(host, Guid.NewGuid(), ReportingTestHelpers.Day15Utc, "50", "90.1", 100m);
        await ReportingTestHelpers.PostAsync(host, Guid.NewGuid(), ReportingTestHelpers.Day15Utc, "91", "50", 40m);

        var month = ReportingTestHelpers.Period;
        var request = new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
        {
            ["from_utc"] = "2026-01-01",
            ["to_utc"] = "2026-01-31"
        });

        async Task<decimal> ProfitAsync()
        {
            if (path == "report")
            {
                var report = await scope.ServiceProvider.GetRequiredService<IIncomeStatementReportReader>()
                    .GetAsync(new IncomeStatementReportRequest { FromInclusive = month, ToInclusive = month });
                return report.NetIncome;
            }

            var sheet = path == "export"
                ? await DirectAccountingExportsTests.RunAsync(host, "accounting.income_statement", request)
                : (await scope.ServiceProvider.GetRequiredService<IReportEngine>()
                    .ExecuteAsync("accounting.income_statement", request, CancellationToken.None)).Sheet;
            return sheet.Rows.Should().ContainSingle(row => row.Cells[0].Display == "Net Income")
                .Which.Cells[1].Value!.Value.GetDecimal();
        }

        (await ProfitAsync()).Should().Be(60m);
        var periods = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await periods.CloseFiscalYearAsync(month, retainedId, "test");
        (await ProfitAsync()).Should().Be(60m, "transferring profit into retained earnings does not erase earned profit");

        // The existing balance/turnover reader must still expose the real closing entries.
        var raw = new List<AccountingStatementAccount>();
        await foreach (var account in scope.ServiceProvider.GetRequiredService<IAccountingStatementAccountReader>()
                           .ReadAsync(month, month, null, CancellationToken.None))
            raw.Add(account);
        var revenue = raw.Single(account => account.Code == "90.1");
        revenue.Debit.Should().Be(100m);
        revenue.Credit.Should().Be(100m);
        revenue.Closing.Should().Be(0m);
        raw.Single(account => account.Code == "300").Closing.Should().Be(-60m);

        await periods.ReopenFiscalYearAsync(month, "test", "Verify report after reopen");
        (await ProfitAsync()).Should().Be(60m);
        await periods.CloseFiscalYearAsync(month, retainedId, "test");
        await periods.CloseMonthAsync(month, "test");
        (await ProfitAsync()).Should().Be(60m);
    }
}
