using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

/// <summary>
/// P1: IAccountByIdResolver is a critical fallback used by runtime services
/// (closing/rebuild/reporting) to handle inactive accounts with historic movements.
/// Contract:
/// - inactive (IsActive=false) accounts must be resolvable
/// - soft-deleted accounts must NOT be resolvable
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountByIdResolver_Contracts_EndToEnd_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetByIdAsync_WhenAccountIsInactive_ReturnsAccount_EvenThoughRuntimeSnapshotExcludesIt()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            id = await accounts.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid),
                CancellationToken.None);

            await accounts.SetActiveAsync(id, isActive: false, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var chartProvider = scope.ServiceProvider.GetRequiredService<IChartOfAccountsProvider>();
            var chart = await chartProvider.GetAsync(CancellationToken.None);

            Action act = () => chart.Get("50");
            act.Should().Throw<AccountNotFoundException>()
                .Which.AssertNgbError(AccountNotFoundException.ErrorCodeConst, "code");

            var resolver = scope.ServiceProvider.GetRequiredService<IAccountByIdResolver>();
            var acc = await resolver.GetByIdAsync(id, CancellationToken.None);

            acc.Should().NotBeNull();
            acc.Id.Should().Be(id);
            acc.Code.Should().Be("50");
            acc.Name.Should().Be("Cash");
        }
    }

    [Fact]
    public async Task GetByIdAsync_WhenAccountIsSoftDeleted_ReturnsNull()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid id;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            id = await accounts.CreateAsync(new CreateAccountRequest(
                Code: "51",
                Name: "Bank",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);

            await accounts.MarkForDeletionAsync(id, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<IAccountByIdResolver>();
            var acc = await resolver.GetByIdAsync(id, CancellationToken.None);
            acc.Should().BeNull("soft-deleted accounts must not be resolvable");
        }
    }

    [Fact]
    public async Task GetByIdsAsync_WhenRequestedIdsIncludeInactiveAndDeleted_ReturnsInactive_SkipsDeletedAndMissing()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid activeId;
        Guid inactiveId;
        Guid deletedId;
        var missingId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            activeId = await accounts.CreateAsync(new CreateAccountRequest(
                Code: "60",
                Name: "AR",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);

            inactiveId = await accounts.CreateAsync(new CreateAccountRequest(
                Code: "61",
                Name: "Inactive asset",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);

            deletedId = await accounts.CreateAsync(new CreateAccountRequest(
                Code: "62",
                Name: "To be deleted",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);

            await accounts.SetActiveAsync(inactiveId, isActive: false, CancellationToken.None);
            await accounts.MarkForDeletionAsync(deletedId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<IAccountByIdResolver>();

            var map = await resolver.GetByIdsAsync([activeId, inactiveId, deletedId, missingId], CancellationToken.None);

            map.Should().ContainKey(activeId);
            map[activeId].Code.Should().Be("60");

            map.Should().ContainKey(inactiveId, "inactive accounts must be resolvable");
            map[inactiveId].Code.Should().Be("61");

            map.Should().NotContainKey(deletedId, "soft-deleted accounts must not be resolvable");
            map.Should().NotContainKey(missingId, "missing ids should be skipped");
        }
    }

    [Fact]
    public async Task GetByIdsAsync_WhenNoIds_ReturnsEmpty()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAccountByIdResolver>();
        var map = await resolver.GetByIdsAsync(Array.Empty<Guid>(), CancellationToken.None);
        map.Should().BeEmpty();
    }
}
