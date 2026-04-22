using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingIdempotencyTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PostAsync_SameDocumentTwice_DoesNotDuplicateEntriesOrTurnovers()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        // Seed minimal CoA.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

            // Cash
            await accounts.CreateAsync(new CreateAccountRequest(
                "50",
                "Cash",
                AccountType.Asset,
                StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);

            // Revenue
            await accounts.CreateAsync(new CreateAccountRequest(
                "90.1",
                "Revenue",
                AccountType.Income,
                StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        // Act
        // IMPORTANT: each PostAsync gets its own DI scope so PostgresUnitOfWork (IAsyncDisposable)
        // is disposed asynchronously and any transaction state cannot leak between calls.
        await PostOnceAsync(host, documentId, period);
        await PostOnceAsync(host, documentId, period); // idempotent repeat

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);

            entries.Should().HaveCount(1);
            entries[0].Amount.Should().Be(100m);
            entries[0].Debit.Code.Should().Be("50");
            entries[0].Credit.Code.Should().Be("90.1");

            var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
            var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);

            var cash = turnovers.Single(x => x.AccountCode == "50");
            cash.DebitAmount.Should().Be(100m);
            cash.CreditAmount.Should().Be(0m);

            var revenue = turnovers.Single(x => x.AccountCode == "90.1");
            revenue.DebitAmount.Should().Be(0m);
            revenue.CreditAmount.Should().Be(100m);
        }
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period)
    {
        // Arrange
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

        // Act
        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                var debit = chart.Get("50");
                var credit = chart.Get("90.1");
                
                ctx.Post(
                    documentId,
                    period,
                    debit,
                    credit,
                    amount: 100m
                );
            },
            ct: CancellationToken.None
        );

        // Assert
        // no-op (assertions happen outside)
    }
}
