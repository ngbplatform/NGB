using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

/// <summary>
/// P0: UpdateAccountRequest must have patch semantics (null = do not change) to avoid dangerous implicit defaults.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsManagement_Update_PatchSemantics_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpdateAsync_WhenOnlyNameProvided_MustNotChangeOtherFields()
    {
        using var host = CreateHost();

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            accountId = await coa.CreateAsync(new CreateAccountRequest(
                    Code: "A1",
                    Name: "My Cash",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    IsContra: true,
                    DimensionRules:
                    [
                        new AccountDimensionRuleRequest("buildings", true, 10),
                        new AccountDimensionRuleRequest("counterparties", false, 20)
                    ],
                    NegativeBalancePolicy: NegativeBalancePolicy.Forbid,
                    IsActive: false),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await coa.UpdateAsync(new UpdateAccountRequest(AccountId: accountId, Name: "My Cash (renamed)"),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var item = await repo.GetAdminByIdAsync(accountId, CancellationToken.None);

            item.Should().NotBeNull();
            item!.Account.Code.Should().Be("A1");
            item.Account.Name.Should().Be("My Cash (renamed)");
            item.Account.Type.Should().Be(AccountType.Asset);
            item.Account.StatementSection.Should().Be(StatementSection.Assets);
            item.Account.IsContra.Should().BeTrue();

            // Dimension rules are represented explicitly.
            item.Account.DimensionRules.Should().HaveCount(2);

            var r1 = item.Account.DimensionRules.Single(r => r.Ordinal == 10);
            r1.DimensionCode.Should().Be("buildings");
            r1.IsRequired.Should().BeTrue();

            var r2 = item.Account.DimensionRules.Single(r => r.Ordinal == 20);
            r2.DimensionCode.Should().Be("counterparties");
            r2.IsRequired.Should().BeFalse();

            item.Account.NegativeBalancePolicy.Should().Be(NegativeBalancePolicy.Forbid);

            // IsActive must remain unchanged.
            item.IsActive.Should().BeFalse();
            item.IsDeleted.Should().BeFalse();
        }
    }

    [Fact]
    public async Task UpdateAsync_WhenOnlyIsActiveProvided_MustNotChangeBusinessFields()
    {
        using var host = CreateHost();

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            accountId = await coa.CreateAsync(new CreateAccountRequest(
                    Code: "A2",
                    Name: "Something",
                    Type: AccountType.Equity,
                    StatementSection: StatementSection.Equity,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                    IsActive: false),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await coa.UpdateAsync(new UpdateAccountRequest(
                    AccountId: accountId,
                    IsActive: true),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var item = await repo.GetAdminByIdAsync(accountId, CancellationToken.None);

            item.Should().NotBeNull();
            item!.IsActive.Should().BeTrue();
            item.Account.Code.Should().Be("A2");
            item.Account.Name.Should().Be("Something");
            item.Account.Type.Should().Be(AccountType.Equity);
            item.Account.StatementSection.Should().Be(StatementSection.Equity);
            item.Account.IsContra.Should().BeFalse();
            item.Account.DimensionRules.Should().BeEmpty();
            item.Account.NegativeBalancePolicy.Should().Be(NegativeBalancePolicy.Allow);
        }
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);
}
