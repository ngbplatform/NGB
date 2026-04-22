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

/// <summary>
/// P1 coverage: NegativeBalancePolicy is an immutable account field once movements exist.
/// (This is easy to accidentally allow during refactors because it "looks like a setting".)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsManagement_Immutability_NegativeBalancePolicy_WithMovements_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UpdateAsync_WhenAccountHasMovements_ForbidsChangingNegativeBalancePolicy_AndDoesNotPersist()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        var cashId = await CreateCashAndRevenueAsync(host, cashPolicy: NegativeBalancePolicy.Allow);
        await PostOnceAsync(host, documentId, period, amount: 100m);

        // Act: attempt to change ONLY NegativeBalancePolicy.
        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await accounts.UpdateAsync(new UpdateAccountRequest(
                AccountId: cashId,
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid, // forbidden once movements exist
                IsActive: true),
                CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<AccountHasMovementsImmutabilityViolationException>();
        ex.Which.AssertNgbError(AccountHasMovementsImmutabilityViolationException.ErrorCodeConst, "accountId", "attemptedChanges");

        ex.Which.Context["attemptedChanges"].Should().BeAssignableTo<IReadOnlyList<string>>();
        var attempted = (IReadOnlyList<string>)ex.Which.Context["attemptedChanges"]!;
        attempted.Should().Contain("NegativeBalancePolicy");

        // Assert: persisted account still has the original policy.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);

            admin.Should().NotBeNull();
            admin!.Account.NegativeBalancePolicy.Should().Be(NegativeBalancePolicy.Allow);
        }
    }

    private static async Task<Guid> CreateCashAndRevenueAsync(IHost host, NegativeBalancePolicy cashPolicy)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: cashPolicy),
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
            ctx.Post(documentId, period, chart.Get("50"), chart.Get("90.1"), amount);
        }, manageTransaction: true, CancellationToken.None);
    }
}
