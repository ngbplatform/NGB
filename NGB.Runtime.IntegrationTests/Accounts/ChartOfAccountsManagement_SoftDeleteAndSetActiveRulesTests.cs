using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsManagement_SoftDeleteAndSetActiveRulesTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MarkForDeletionAsync_WhenAccountHasMovements_Throws_AndDoesNotDelete()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        var cashId = await CreateCashAndRevenueAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await accounts.MarkForDeletionAsync(cashId, CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<AccountHasMovementsCannotDeleteException>();
        ex.Which.AssertNgbError(AccountHasMovementsCannotDeleteException.ErrorCodeConst, "accountId");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();
            admin!.IsDeleted.Should().BeFalse();
            admin.IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public async Task MarkForDeletionAsync_WhenAccountHasNoMovements_MarksDeletedAndInactive()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var cashId = await CreateCashOnlyAsync(host);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await accounts.MarkForDeletionAsync(cashId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();
            admin!.IsDeleted.Should().BeTrue();
            admin.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task SetActiveAsync_WhenAccountIsDeleted_Throws_AndDoesNotChangeState()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var cashId = await CreateCashOnlyAsync(host);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await accounts.MarkForDeletionAsync(cashId, CancellationToken.None);
        }

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await accounts.SetActiveAsync(cashId, isActive: true, CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<AccountDeletedException>();
        ex.Which.AssertNgbError(AccountDeletedException.ErrorCodeConst, "accountId");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();
            admin!.IsDeleted.Should().BeTrue();
            admin.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task MarkForDeletionAsync_WhenAlreadyDeleted_IsNoOp()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var cashId = await CreateCashOnlyAsync(host);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await accounts.MarkForDeletionAsync(cashId, CancellationToken.None);
            await accounts.MarkForDeletionAsync(cashId, CancellationToken.None); // should not throw
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();
            admin!.IsDeleted.Should().BeTrue();
            admin.IsActive.Should().BeFalse();
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
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid),
            CancellationToken.None);
    }

    private static async Task<Guid> CreateCashAndRevenueAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Forbid),
            CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        return cashId;
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await engine.PostAsync(PostingOperation.Post, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            var cash = chart.Get("50");
            var revenue = chart.Get("90.1");
            ctx.Post(documentId, period, cash, revenue, amount);
        }, manageTransaction: true, CancellationToken.None);
    }
}
