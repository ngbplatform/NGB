using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

/// <summary>
/// P1: Admin UI needs to browse inactive and (optionally) deleted accounts.
/// These behaviors must remain stable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsAdminService_EndToEnd_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetAsync_IncludeDeletedFalse_ExcludesDeleted_ButIncludesInactive()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Arrange
        Guid activeId;
        Guid inactiveId;
        Guid deletedId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            activeId = await mgmt.CreateAsync(new CreateAccountRequest(
                Code: "A100",
                Name: "Active",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true
            ), CancellationToken.None);

            inactiveId = await mgmt.CreateAsync(new CreateAccountRequest(
                Code: "A200",
                Name: "Inactive",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: false
            ), CancellationToken.None);

            deletedId = await mgmt.CreateAsync(new CreateAccountRequest(
                Code: "A300",
                Name: "Deleted",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true
            ), CancellationToken.None);

            await mgmt.MarkForDeletionAsync(deletedId, CancellationToken.None);
        }

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IChartOfAccountsAdminService>();
            var list = await admin.GetAsync(includeDeleted: false, CancellationToken.None);

            var codes = list.Select(x => x.Account.Code).ToArray();

            codes.Should().Contain("A100");
            codes.Should().Contain("A200");
            codes.Should().NotContain("A300");

            var a100 = list.Single(x => x.Account.Code == "A100");
            a100.IsDeleted.Should().BeFalse();
            a100.IsActive.Should().BeTrue();

            var a200 = list.Single(x => x.Account.Code == "A200");
            a200.IsDeleted.Should().BeFalse();
            a200.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task GetAsync_IncludeDeletedTrue_IncludesDeleted_AndShowsFlags()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Arrange
        Guid deletedId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await mgmt.CreateAsync(new CreateAccountRequest(
                Code: "B100",
                Name: "Active",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true
            ), CancellationToken.None);

            deletedId = await mgmt.CreateAsync(new CreateAccountRequest(
                Code: "B200",
                Name: "Will be deleted",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true
            ), CancellationToken.None);

            await mgmt.MarkForDeletionAsync(deletedId, CancellationToken.None);
        }

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IChartOfAccountsAdminService>();
            var list = await admin.GetAsync(includeDeleted: true, CancellationToken.None);

            list.Select(x => x.Account.Code).Should().Contain(new[] { "B100", "B200" });

            var b200 = list.Single(x => x.Account.Code == "B200");
            b200.IsDeleted.Should().BeTrue();
            b200.IsActive.Should().BeFalse("repository sets is_active=false on soft delete");
        }
    }
}
