using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_RequiresPriorMonthsClosedTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYearAsync_WhenAnyPriorMonthIsNotClosed_ShouldThrow()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // FY close posts entries into the OPEN end month; all prior months of that fiscal year must be closed.
        var fiscalYearEndPeriod = new DateOnly(2025, 12, 1);

        await SeedCoaAsync(host);

        // Close Jan-Oct, but leave Nov intentionally open to trigger the guard.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

            for (var p = new DateOnly(2025, 1, 1); p <= new DateOnly(2025, 10, 1); p = p.AddMonths(1))
                await closing.CloseMonthAsync(p, closedBy: "test", ct: CancellationToken.None);
        }

        // Act
        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            var retainedEarningsId = await GetAccountIdAsync(scope.ServiceProvider, code: "300");

            await closing.CloseFiscalYearAsync(
                fiscalYearEndPeriod: fiscalYearEndPeriod,
                retainedEarningsAccountId: retainedEarningsId,
                closedBy: "test",
                ct: CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<FiscalYearClosingPrerequisiteNotMetException>()
            .WithMessage("*requires all prior months to be closed*Not closed: 2025-11-01*");
    }

    private static async Task SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Minimal CoA for FY close validation (Retained Earnings in Equity).
        await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "300",
                Name: "Retained Earnings",
                Type: AccountType.Equity,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ),
            CancellationToken.None);
    }

    private static async Task<Guid> GetAccountIdAsync(IServiceProvider sp, string code)
    {
        var repo = sp.GetRequiredService<NGB.Persistence.Accounts.IChartOfAccountsRepository>();
        var rows = await repo.GetForAdminAsync(includeDeleted: true);
        var row = rows.Single(r => r.Account.Code == code && !r.IsDeleted);
        return row.Account.Id;
    }
}
