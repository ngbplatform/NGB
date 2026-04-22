using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

/// <summary>
/// P0: Dimension rules (accounting_account_dimension_rules) persistence and roundtrip.
///
/// NOTE: CoA requests use explicit dimension rules (accounting_account_dimension_rules).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccounts_DimensionRules_Persistence_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_Persists_DimensionRules_And_PlatformDimensions()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var accountId = await svc.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: NGB.Accounting.Accounts.AccountType.Asset,
            DimensionRules:
            [
                new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                new AccountDimensionRuleRequest("Counterparties", IsRequired: false, Ordinal: 20)
            ]
        ), CancellationToken.None);

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var codeNorm1 = "buildings";
        var codeNorm2 = "counterparties";

        var dimId1 = DeterministicGuid.Create($"Dimension|{codeNorm1}");
        var dimId2 = DeterministicGuid.Create($"Dimension|{codeNorm2}");

        var dims = (await uow.Connection.QueryAsync<(Guid Id, string Code)>(
            new CommandDefinition(
                "SELECT dimension_id AS Id, code AS Code FROM platform_dimensions WHERE dimension_id = ANY(@Ids) ORDER BY code;",
                new { Ids = new[] { dimId1, dimId2 } },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None)))
            .ToList();

        dims.Select(x => x.Id).Should().Equal(new[] { dimId1, dimId2 }, "dimensions are created deterministically by normalized code");
        dims.Select(x => x.Code).Should().Equal("Buildings", "Counterparties");

        var rules = (await uow.Connection.QueryAsync<(Guid AccountId, Guid DimensionId, int Ordinal, bool IsRequired)>(
            new CommandDefinition(
                "SELECT account_id AS AccountId, dimension_id AS DimensionId, ordinal AS Ordinal, is_required AS IsRequired FROM accounting_account_dimension_rules WHERE account_id = @AccountId ORDER BY ordinal;",
                new { AccountId = accountId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None)))
            .ToList();

        rules.Should().HaveCount(2);
        rules[0].DimensionId.Should().Be(dimId1);
        rules[0].Ordinal.Should().Be(10);
        rules[0].IsRequired.Should().BeTrue();

        rules[1].DimensionId.Should().Be(dimId2);
        rules[1].Ordinal.Should().Be(20);
        rules[1].IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Update_Replaces_DimensionRules()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var accountId = await svc.CreateAsync(new CreateAccountRequest(
            Code: "62",
            Name: "AR",
            Type: NGB.Accounting.Accounts.AccountType.Asset,
            DimensionRules:
            [
                new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                new AccountDimensionRuleRequest("Counterparties", IsRequired: true, Ordinal: 20)
            ]
        ), CancellationToken.None);

        // Drop the second rule.
        await svc.UpdateAsync(new UpdateAccountRequest(
            AccountId: accountId,
            DimensionRules:
            [
                new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10)
            ]
        ), CancellationToken.None);

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var rules = (await uow.Connection.QueryAsync<(Guid DimensionId, int Ordinal)>(
            new CommandDefinition(
                "SELECT dimension_id AS DimensionId, ordinal AS Ordinal FROM accounting_account_dimension_rules WHERE account_id = @AccountId ORDER BY ordinal;",
                new { AccountId = accountId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None)))
            .ToList();

        rules.Should().HaveCount(1);
        rules[0].Ordinal.Should().Be(10);
        rules[0].DimensionId.Should().Be(DeterministicGuid.Create("Dimension|buildings"));
    }

    [Fact]
    public async Task Update_NameOnly_DoesNotDrop_AdditionalDimensionRules()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var accountId = await svc.CreateAsync(new CreateAccountRequest(
            Code: "77",
            Name: "With many dimension rules",
            Type: NGB.Accounting.Accounts.AccountType.Asset,
            DimensionRules: new[]
            {
                new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 1),
                new AccountDimensionRuleRequest("Counterparties", IsRequired: true, Ordinal: 2),
                new AccountDimensionRuleRequest("Contracts", IsRequired: false, Ordinal: 3),
                new AccountDimensionRuleRequest("Floors", IsRequired: false, Ordinal: 4),
                new AccountDimensionRuleRequest("Units", IsRequired: false, Ordinal: 5)
            }
        ), CancellationToken.None);

        await svc.UpdateAsync(new UpdateAccountRequest(
            AccountId: accountId,
            Name: "Renamed only"),
            CancellationToken.None);

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var cnt = await uow.Connection.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM accounting_account_dimension_rules WHERE account_id = @AccountId;",
                new { AccountId = accountId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

        cnt.Should().Be(5, "update operations must not accidentally collapse dimension rules back to 3 slots");
    }
}
