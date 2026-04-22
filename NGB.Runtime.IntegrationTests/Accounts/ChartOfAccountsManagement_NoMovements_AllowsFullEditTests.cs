using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsManagement_NoMovements_AllowsFullEditTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpdateAsync_WhenAccountHasNoMovements_AllowsFullEdit_AndPersists()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var cashId = await CreateCashOnlyAsync(host);

        // Warm up a scope snapshot BEFORE the update.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart = await provider.GetAsync(CancellationToken.None);
            chart.Get("50").Id.Should().Be(cashId);
        }

        // Act: update ALL fields that are immutable when movements exist.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await accounts.UpdateAsync(new UpdateAccountRequest(
                AccountId: cashId,
                Code: "50X",
                Name: "Cash (fully edited)",
                Type: AccountType.Liability,
                StatementSection: StatementSection.Liabilities,
                IsContra: true,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest("Counterparty", true, Ordinal: 10),
                    new AccountDimensionRuleRequest("Contract", false, Ordinal: 20),
                    new AccountDimensionRuleRequest("Project", true, Ordinal: 30)
                ],
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid,
                // Keep active: runtime snapshots exclude inactive accounts (covered elsewhere).
                IsActive: true),
                CancellationToken.None);
        }

        // Assert: persisted in DB.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();

            admin!.Account.Code.Should().Be("50X");
            admin.Account.Name.Should().Be("Cash (fully edited)");
            admin.Account.Type.Should().Be(AccountType.Liability);
            admin.Account.StatementSection.Should().Be(StatementSection.Liabilities);
            admin.Account.IsContra.Should().BeTrue();

            // Dimension rules are persisted as accounting_account_dimension_rules.
            admin.Account.DimensionRules.Should().HaveCount(3);

            var r1 = admin.Account.DimensionRules.Single(r => r.Ordinal == 10);
            r1.DimensionCode.Should().Be("Counterparty");
            r1.IsRequired.Should().BeTrue();

            var r2 = admin.Account.DimensionRules.Single(r => r.Ordinal == 20);
            r2.DimensionCode.Should().Be("Contract");
            r2.IsRequired.Should().BeFalse();

            var r3 = admin.Account.DimensionRules.Single(r => r.Ordinal == 30);
            r3.DimensionCode.Should().Be("Project");
            r3.IsRequired.Should().BeTrue();

            admin.Account.NegativeBalancePolicy.Should().Be(NegativeBalancePolicy.Forbid);
            admin.IsActive.Should().BeTrue();
            admin.IsDeleted.Should().BeFalse();
        }

        // Assert: new scopes see the updated code; old code is no longer resolvable.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart = await provider.GetAsync(CancellationToken.None);

            chart.Get("50X").Id.Should().Be(cashId);
            Action act = () => chart.Get("50");
            act.Should().Throw<AccountNotFoundException>()
                .Which.AssertNgbError(AccountNotFoundException.ErrorCodeConst, "code");
        }
    }

    [Fact]
    public async Task UpdateAsync_WhenDeactivated_RuntimeSnapshotMustNotResolve_AndAdminShowsInactive()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var cashId = await CreateCashOnlyAsync(host);

        // First: ensure we have a renamed code that is resolvable when active.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await accounts.UpdateAsync(new UpdateAccountRequest(
                    AccountId: cashId,
                    Code: "50X",
                    Name: "Cash",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                    IsActive: true),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart = await provider.GetAsync(CancellationToken.None);
            chart.Get("50X").Id.Should().Be(cashId);
        }

        // Act: deactivate.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await accounts.UpdateAsync(new UpdateAccountRequest(
                    AccountId: cashId,
                    Code: "50X",
                    Name: "Cash (inactive)",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                    IsActive: false),
                CancellationToken.None);
        }

        // Assert: admin view sees the account as inactive.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();
            admin!.IsActive.Should().BeFalse();
            admin.IsDeleted.Should().BeFalse();
        }

        // Assert: runtime snapshot excludes inactive, so code cannot be resolved.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart = await provider.GetAsync(CancellationToken.None);

            Action act = () => chart.Get("50X");
            act.Should().Throw<AccountNotFoundException>()
                .Which.AssertNgbError(AccountNotFoundException.ErrorCodeConst, "code");
        }
    }

    private static async Task<Guid> CreateCashOnlyAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        return await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            IsActive: true),
            CancellationToken.None);
    }
}
