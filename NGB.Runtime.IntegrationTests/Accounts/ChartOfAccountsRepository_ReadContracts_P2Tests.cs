using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

/// <summary>
/// P2: Postgres ChartOfAccounts repository read-side contract tests.
/// These tests lock in:
/// - filtering rules for runtime snapshots (GetAll)
/// - admin list ordering and includeDeleted behavior
/// - GetCodeById and HasMovements semantics (used by immutability rules and UX).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsRepository_ReadContracts_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetAllAsync_FiltersToActiveAndNotDeletedOnly()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Active.
        await svc.CreateAsync(new CreateAccountRequest(
            Code: "A1",
            Name: "Active",
            Type: AccountType.Asset,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Inactive.
        var inactiveId = await svc.CreateAsync(new CreateAccountRequest(
            Code: "A2",
            Name: "Inactive",
            Type: AccountType.Asset,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            IsActive: false
        ), CancellationToken.None);

        // Deleted.
        var deletedId = await svc.CreateAsync(new CreateAccountRequest(
            Code: "A3",
            Name: "Deleted",
            Type: AccountType.Asset,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
        await svc.MarkForDeletionAsync(deletedId, CancellationToken.None);

        var snapshot = await repo.GetAllAsync(CancellationToken.None);

        snapshot.Select(a => a.Code).Should().BeEquivalentTo(new[] { "A1" }, o => o.WithStrictOrdering());

        // Sanity: inactive/deleted still exist for admin.
        (await repo.GetAdminByIdAsync(inactiveId)).Should().NotBeNull();
        (await repo.GetAdminByIdAsync(deletedId)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetForAdminAsync_OrdersByCode_AndIncludeDeletedTogglesVisibility()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var aId = await svc.CreateAsync(new CreateAccountRequest("A", "A", AccountType.Asset), CancellationToken.None);
        var bId = await svc.CreateAsync(new CreateAccountRequest("B", "B", AccountType.Asset), CancellationToken.None);
        var cId = await svc.CreateAsync(new CreateAccountRequest("C", "C", AccountType.Asset), CancellationToken.None);

        await svc.MarkForDeletionAsync(bId, CancellationToken.None);

        var withoutDeleted = await repo.GetForAdminAsync(includeDeleted: false, ct: CancellationToken.None);
        withoutDeleted.Select(x => x.Account.Code).Should().Equal("A", "C");

        var withDeleted = await repo.GetForAdminAsync(includeDeleted: true, ct: CancellationToken.None);
        withDeleted.Select(x => x.Account.Code)
            .Should()
            .Equal(new[] { "A", "B", "C" }, "repository orders by raw code ASC");

        withDeleted.Single(x => x.Account.Id == bId).IsDeleted.Should().BeTrue();
        withDeleted.Single(x => x.Account.Id == aId).IsDeleted.Should().BeFalse();
        withDeleted.Single(x => x.Account.Id == cId).IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetAdminByIdAsync_ReturnsNull_WhenMissing()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var missing = await repo.GetAdminByIdAsync(Guid.CreateVersion7(), CancellationToken.None);
        missing.Should().BeNull();
    }

    [Fact]
    public async Task GetCodeByIdAsync_ReturnsNull_WhenSoftDeleted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var id = await svc.CreateAsync(new CreateAccountRequest("X1", "Temp", AccountType.Asset), CancellationToken.None);

        (await repo.GetCodeByIdAsync(id, CancellationToken.None)).Should().Be("X1");

        await svc.MarkForDeletionAsync(id, CancellationToken.None);

        (await repo.GetCodeByIdAsync(id, CancellationToken.None)).Should().BeNull("soft-deleted accounts should not be resolvable by id for runtime/reporting");
    }

    [Fact]
    public async Task HasMovementsAsync_FlipsToTrue_AfterAnyRegisterEntryReferencesAccount()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        (await repo.HasMovementsAsync(cashId, CancellationToken.None)).Should().BeFalse();

        await ReportingTestHelpers.PostAsync(
            host,
            documentId: Guid.CreateVersion7(),
            dateUtc: ReportingTestHelpers.Day1Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 10m);

        (await repo.HasMovementsAsync(cashId, CancellationToken.None)).Should().BeTrue();
    }
}
