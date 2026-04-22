using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.BalanceSheet;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class BalanceSheet_ZeroAccounts_Matrix_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task BalanceSheet_IncludeZeroAccounts_TogglesPresenceOfZeroLines()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Seed minimal CoA (Cash) + an extra Asset account, then create movements that net to zero.
        // This ensures rows exist in turnovers/balances, so IncludeZeroAccounts can actually toggle visibility.
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Add one more Asset account (Bank) with no net balance.
        await using (var seedScope = host.Services.CreateAsyncScope())
        {
            var repo = seedScope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var svc = seedScope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == "51" && !a.IsDeleted);

            if (existing is null)
            {
                await svc.CreateAsync(
                    new CreateAccountRequest(
                        Code: "51",
                        Name: "Bank",
                        Type: AccountType.Asset,
                        IsContra: false,
                        NegativeBalancePolicy: NegativeBalancePolicy.Allow
                    ),
                    CancellationToken.None);
            }
            else if (!existing.IsActive)
            {
                await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);
            }
        }

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        // Move Cash -> Bank, then back. Both end up with zero closing balances.
        await ReportingTestHelpers.PostAsync(host, doc1, ReportingTestHelpers.Day1Utc, debitCode: "51", creditCode: "50", amount: 100m);
        await ReportingTestHelpers.PostAsync(host, doc2, ReportingTestHelpers.Day2Utc, debitCode: "50", creditCode: "51", amount: 100m);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IBalanceSheetReportReader>();

        var reportNoZeros = await reader.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = ReportingTestHelpers.Period,
            IncludeZeroAccounts = false
        }, CancellationToken.None);

        var reportWithZeros = await reader.GetAsync(new BalanceSheetReportRequest
        {
            AsOfPeriod = ReportingTestHelpers.Period,
            IncludeZeroAccounts = true
        }, CancellationToken.None);

        var noZeroLinesCount = reportNoZeros.Sections.Sum(s => s.Lines.Count);
        var withZeroLinesCount = reportWithZeros.Sections.Sum(s => s.Lines.Count);

        noZeroLinesCount.Should().Be(0, "all accounts close to zero and IncludeZeroAccounts=false should hide them");
        withZeroLinesCount.Should().BeGreaterThan(0, "IncludeZeroAccounts=true should include zero-balance accounts that had movements");
    }
}
