using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsProvider_CacheInvalidation_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task NewScope_SeesChartOfAccountsUpdates_AfterManagementChanges()
    {
        // Arrange
        using var host = CreateHost();

        // Seed a minimal chart.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid));
        }

        // Load chart in scope1 (creates provider instance + caches snapshot).
        ChartOfAccounts chart1;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            chart1 = await provider.GetAsync();
            chart1.TryGetByCode("51", out _).Should().BeFalse();
        }

        // Change chart via management service in another scope.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "51",
                Name: "Bank",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid));
        }

        // Act + Assert: a NEW scope must observe the updated CoA.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart2 = await provider.GetAsync();

            chart2.TryGetByCode("51", out var bank).Should().BeTrue();
            bank!.Code.Should().Be("51");
        }
    }

    [Fact]
    public async Task SameScope_ReturnsSnapshot_DoesNotAutoRefreshAfterManagementChanges()
    {
        // Arrange
        using var host = CreateHost();

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();
        var provider = sp.GetRequiredService<IChartOfAccountsProvider>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid));

        // Act: load and cache the snapshot.
        var chartBefore = await provider.GetAsync();
        chartBefore.TryGetByCode("51", out _).Should().BeFalse();

        // Change CoA within the same scope.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "51",
            Name: "Bank",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid));

        // Assert: provider returns the same cached snapshot (no auto-refresh in the same scope).
        var chartAfter = await provider.GetAsync();
        ReferenceEquals(chartBefore, chartAfter).Should().BeTrue();

        Action act = () => chartAfter.Get("51");
        act.Should().Throw<AccountNotFoundException>()
            .Which.AssertNgbError(AccountNotFoundException.ErrorCodeConst, "code");
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);
}
