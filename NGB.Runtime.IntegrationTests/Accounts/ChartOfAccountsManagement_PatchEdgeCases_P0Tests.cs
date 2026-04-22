using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsManagement_DimensionRules_PatchEdgeCases_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpdateAsync_WhenDimensionRulesIsNull_DoesNotChangeExistingRules()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            accountId = await coa.CreateAsync(
                new CreateAccountRequest(
                    Code: "A100",
                    Name: "Account",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                    DimensionRules:
                    [
                        new AccountDimensionRuleRequest(Ordinal: 10, DimensionCode: "building", IsRequired: true),
                        new AccountDimensionRuleRequest(Ordinal: 20, DimensionCode: "unit", IsRequired: false),
                        new AccountDimensionRuleRequest(Ordinal: 30, DimensionCode: "contract", IsRequired: false)
                    ]));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

            await coa.UpdateAsync(new UpdateAccountRequest(AccountId: accountId, Name: "Updated", DimensionRules: null));

            var item = await repo.GetAdminByIdAsync(accountId);
            item.Should().NotBeNull();
            item!.Account.DimensionRules.Select(x => x.Ordinal).Should().Equal(10, 20, 30);
        }
    }

    [Fact]
    public async Task UpdateAsync_WhenDimensionRulesIsWhitespaceOnly_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var id = await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "A100",
                Name: "Account",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                DimensionRules: [new AccountDimensionRuleRequest(Ordinal: 10, DimensionCode: "building", IsRequired: true)]));

        var act = async () =>
            await coa.UpdateAsync(new UpdateAccountRequest(AccountId: id, Name: "Updated", DimensionRules:
            [
                new AccountDimensionRuleRequest(Ordinal: 10, DimensionCode: " ", IsRequired: true)
            ]));

        var ex = await act.Should().ThrowAsync<AccountDimensionRulesValidationException>();
        ex.Which.AssertNgbError(AccountDimensionRulesValidationException.ErrorCodeConst, "index");
    }

    [Fact]
    public async Task UpdateAsync_WhenDimensionRulesIsEmpty_ClearsAllRules()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            accountId = await coa.CreateAsync(
                new CreateAccountRequest(
                    Code: "A100",
                    Name: "Account",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                    DimensionRules:
                    [
                        new AccountDimensionRuleRequest(Ordinal: 10, DimensionCode: "building", IsRequired: true),
                        new AccountDimensionRuleRequest(Ordinal: 20, DimensionCode: "unit", IsRequired: false)
                    ]));
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

            await coa.UpdateAsync(new UpdateAccountRequest(AccountId: accountId, Name: "Updated", DimensionRules: []));

            var item = await repo.GetAdminByIdAsync(accountId);
            item.Should().NotBeNull();
            item!.Account.DimensionRules.Should().BeEmpty();
        }
    }
}
