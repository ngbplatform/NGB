using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// P1: AccountingEntryReader must NOT depend on ChartOfAccounts snapshots.
/// It should hydrate Account metadata via a join to accounting_accounts.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountingEntryReader_DoesNotUseChartSnapshot_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetByDocumentAsync_DoesNotCall_ChartOfAccountsProvider()
    {
        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 2, 1, 0, 30, 0, DateTimeKind.Utc);

        // Arrange: create accounts + post a register entry using a normal host.
        using (var host = IntegrationHostFactory.Create(Fixture.ConnectionString))
        {
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

                await mgmt.CreateAsync(new CreateAccountRequest(
                    Code: "CASH",
                    Name: "Cash",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                    IsActive: true
                ), CancellationToken.None);

                await mgmt.CreateAsync(new CreateAccountRequest(
                    Code: "REV",
                    Name: "Revenue",
                    Type: AccountType.Income,
                    StatementSection: StatementSection.Income,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                    IsActive: true
                ), CancellationToken.None);
            }

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

                await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);

                    var debit = chart.Get("CASH");
                    var credit = chart.Get("REV");

                    ctx.Post(
                        documentId: documentId,
                        period: periodUtc,
                        debit: debit,
                        credit: credit,
                        amount: 10m
                    );
                }, CancellationToken.None);
            }
        }

        // Act + Assert: replace provider with a throwing stub.
        // If AccountingEntryReader tries to load the chart snapshot, this test fails.
        using (var host = IntegrationHostFactory.Create(
                   Fixture.ConnectionString,
                   services => services.AddScoped<IChartOfAccountsProvider, ThrowingChartOfAccountsProvider>()))
        {
            await using var scope = host.Services.CreateAsyncScope();

            var reader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();

            var entries = await reader.GetByDocumentAsync(documentId, CancellationToken.None);

            entries.Should().HaveCount(1);
            entries[0].Debit.Code.Should().Be("CASH");
            entries[0].Credit.Code.Should().Be("REV");
        }
    }

    private sealed class ThrowingChartOfAccountsProvider : IChartOfAccountsProvider
    {
        public Task<ChartOfAccounts> GetAsync(CancellationToken ct = default)
            => throw new NotSupportedException(
                "ChartOfAccountsProvider must not be called by AccountingEntryReader.");
    }
}
