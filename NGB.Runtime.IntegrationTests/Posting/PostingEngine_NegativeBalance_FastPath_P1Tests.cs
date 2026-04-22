using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// P1: When all involved accounts have NegativeBalancePolicy.Allow,
/// PostingEngine must skip operational balance DB reads entirely.
/// This test replaces IAccountingOperationalBalanceReader with a fail-fast implementation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_NegativeBalance_FastPath_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_WhenAllAccountsAllow_NeverCallsOperationalBalanceReader()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                // Override operational reader: if PostingEngine calls it, the test must fail.
                services.AddScoped<IAccountingOperationalBalanceReader, FailIfCalledOperationalBalanceReader>();
            });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await accounts.CreateAsync(new CreateAccountRequest(
                "50",
                "Cash",
                AccountType.Asset,
                StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                "90.1",
                "Revenue",
                AccountType.Income,
                StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);
        }

        var period = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await engine.PostAsync(PostingOperation.Post, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, period, chart.Get("50"), chart.Get("90.1"), 10m);
            }, manageTransaction: true, CancellationToken.None);
        }

        // Posting succeeded => operational reader was not called.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
            var entries = await reader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);
        }
    }

    private sealed class FailIfCalledOperationalBalanceReader : IAccountingOperationalBalanceReader
    {
        public Task<IReadOnlyList<AccountingOperationalBalanceSnapshot>> GetForKeysAsync(
            DateOnly period,
            IReadOnlyList<AccountingBalanceKey> keys,
            CancellationToken ct = default)
        {
            throw new NotSupportedException(
                "IAccountingOperationalBalanceReader must not be called when all involved accounts have NegativeBalancePolicy.Allow.");
        }
    }
}
