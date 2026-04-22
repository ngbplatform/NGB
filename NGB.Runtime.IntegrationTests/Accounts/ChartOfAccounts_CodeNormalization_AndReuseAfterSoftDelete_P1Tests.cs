using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

/// <summary>
/// P1: CoA code normalization uses a generated column code_norm = lower(trim(code))
/// with a unique index filtered by is_deleted = FALSE.
/// These tests lock in case-insensitive uniqueness and "reuse after soft-delete" behavior.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccounts_CodeNormalization_AndReuseAfterSoftDelete_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAsync_TwoCodesThatNormalizeSame_SecondFailsWithUniqueViolation()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await mgmt.CreateAsync(new CreateAccountRequest(
            Code: "X1",
            Name: "One",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        Func<Task> act = () => mgmt.CreateAsync(new CreateAccountRequest(
            Code: "x1",
            Name: "Duplicate by case",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        ex.Which.ConstraintName.Should().Be("ux_acc_accounts_code_norm");
    }

    [Fact]
    public async Task CreateAsync_MarkForDeletion_ThenCreateSameNormalizedCode_FailsWithUniqueViolation()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid firstId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            firstId = await mgmt.CreateAsync(new CreateAccountRequest(
                Code: "C100",
                Name: "First",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true
            ), CancellationToken.None);

            await mgmt.MarkForDeletionAsync(firstId, CancellationToken.None);

            Func<Task> act = () => mgmt.CreateAsync(new CreateAccountRequest(
                Code: "c100",
                Name: "Should fail (code reuse not allowed)",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true
            ), CancellationToken.None);

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
            ex.Which.ConstraintName.Should().Be("ux_acc_accounts_code_norm");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IChartOfAccountsAdminService>();

            var includeDeleted = await admin.GetAsync(includeDeleted: true, CancellationToken.None);
            includeDeleted.Select(x => x.Account.Id).Should().ContainSingle(id => id == firstId);
            includeDeleted.Single(x => x.Account.Id == firstId).IsDeleted.Should().BeTrue();
        }
    }
}
