using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsManagement_Immutability_WithMovementsTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpdateAsync_WhenAccountHasMovements_ForbidsImmutableFieldChanges_AndDoesNotPersist()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        var cashId = await CreateCashAndRevenueAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Act: try to change immutable fields (Code + StatementSection).
        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await accounts.UpdateAsync(new UpdateAccountRequest(
                AccountId: cashId,
                Code: "50X", // forbidden
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Equity, // forbidden
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid,
                IsActive: true),
                CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<AccountHasMovementsImmutabilityViolationException>();
        ex.Which.AssertNgbError(AccountHasMovementsImmutabilityViolationException.ErrorCodeConst, "accountId", "attemptedChanges");

        ex.Which.Context["attemptedChanges"].Should().BeAssignableTo<IReadOnlyList<string>>();
        var attempted = (IReadOnlyList<string>)ex.Which.Context["attemptedChanges"]!;
        attempted.Should().Contain("Code");
        attempted.Should().Contain("StatementSection");

        // Assert: nothing persisted.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();
            admin!.Account.Code.Should().Be("50");
            admin.Account.StatementSection.Should().Be(StatementSection.Assets);
        }
    }

    [Fact]
    public async Task UpdateAsync_WhenAccountHasMovements_AllowsNameAndIsActive_AndPersists()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        var cashId = await CreateCashAndRevenueAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Act: change only allowed fields.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await accounts.UpdateAsync(new UpdateAccountRequest(
                AccountId: cashId,
                Code: "50",
                Name: "Cash (renamed)", // allowed
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid,
                IsActive: false), // allowed
                CancellationToken.None);
        }

        // Assert: persisted.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();
            admin!.Account.Code.Should().Be("50");
            admin.Account.Name.Should().Be("Cash (renamed)");
            admin.IsActive.Should().BeFalse();

            // And immutable fields stayed the same.
            admin.Account.Type.Should().Be(AccountType.Asset);
            admin.Account.StatementSection.Should().Be(StatementSection.Assets);
            admin.Account.NegativeBalancePolicy.Should().Be(NegativeBalancePolicy.Forbid);
        }
    }

    [Fact]
    public async Task UpdateAsync_WhenAccountHasMovements_AllowsCashFlowMetadata_AndPersists()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        var cashId = await CreateCashAndRevenueAsync(host);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await accounts.UpdateAsync(new UpdateAccountRequest(
                AccountId: cashId,
                CashFlowRole: CashFlowRole.WorkingCapital,
                CashFlowLineCode: CashFlowSystemLineCodes.WorkingCapitalPrepaids),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();
            admin!.Account.CashFlowRole.Should().Be(CashFlowRole.WorkingCapital);
            admin.Account.CashFlowLineCode.Should().Be(CashFlowSystemLineCodes.WorkingCapitalPrepaids);
        }
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
        var sp = scope.ServiceProvider;

        var engine = sp.GetRequiredService<PostingEngine>();

        await engine.PostAsync(PostingOperation.Post, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            var cash = chart.Get("50");
            var revenue = chart.Get("90.1");
            ctx.Post(documentId, period, cash, revenue, amount);
        }, manageTransaction: true, CancellationToken.None);

        // sanity: turnovers are monthly
        _ = AccountingPeriod.FromDateTime(period);
    }
}
