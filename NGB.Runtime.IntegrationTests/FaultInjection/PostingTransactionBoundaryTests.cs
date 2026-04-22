using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

[Collection(PostgresCollection.Name)]
public sealed class PostingTransactionBoundaryTests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task PostAsync_WhenEntryWriterThrowsAfterWriting_RollsBackEverything_AndDoesNotWritePostingLog()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services =>
            {
                // Replace the default Postgres writer with a test wrapper that throws AFTER calling the real writer.
                ReplaceAccountingEntryWriter(services);
            });

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalChartOfAccountsAsync(host);

        // Act
        var act = () => PostOnceAsync(host, documentId, period);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Simulated failure after writing entries*");

        await AssertNoSideEffectsAsync(host, documentId, period, PostingOperation.Post);
    }

    [Fact]
    public async Task PostAsync_WhenTurnoverWriterThrowsAfterWriting_RollsBackEverything_AndDoesNotWritePostingLog()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            fixture.ConnectionString,
            services =>
            {
                ReplaceAccountingTurnoverWriter(services);
            });

        var period = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalChartOfAccountsAsync(host);

        // Act
        var act = () => PostOnceAsync(host, documentId, period);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Simulated failure after writing turnovers*");

        await AssertNoSideEffectsAsync(host, documentId, period, PostingOperation.Post);
    }

    private static async Task SeedMinimalChartOfAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
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

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var posting = sp.GetRequiredService<PostingEngine>();

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
    }

    private static async Task AssertNoSideEffectsAsync(IHost host, Guid documentId, DateTime period, PostingOperation operation)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().BeEmpty();

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(DateOnly.FromDateTime(period), CancellationToken.None);
        turnovers.Should().BeEmpty();

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            PageSize = 50,
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = operation
        }, CancellationToken.None);

        page.Records.Should().BeEmpty("posting_log must be atomic with accounting writes; a failing transaction must not leave audit garbage");
    }

    private static void ReplaceAccountingEntryWriter(IServiceCollection services)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(IAccountingEntryWriter));
        services.Remove(descriptor);

        // Register the real Postgres writer as a concrete type so the wrapper can call it.
        services.AddScoped<PostgresAccountingEntryWriter>();

        services.AddScoped<IAccountingEntryWriter>(sp =>
            new ThrowAfterEntriesWriter(sp.GetRequiredService<PostgresAccountingEntryWriter>()));
    }

    private static void ReplaceAccountingTurnoverWriter(IServiceCollection services)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(IAccountingTurnoverWriter));
        services.Remove(descriptor);

        services.AddScoped<PostgresAccountingTurnoverWriter>();

        services.AddScoped<IAccountingTurnoverWriter>(sp =>
            new ThrowAfterTurnoversWriter(sp.GetRequiredService<PostgresAccountingTurnoverWriter>()));
    }

    private sealed class ThrowAfterEntriesWriter(IAccountingEntryWriter inner) : IAccountingEntryWriter
    {
        public async Task WriteAsync(IReadOnlyList<NGB.Accounting.Registers.AccountingEntry> entries, CancellationToken ct = default)
        {
            await inner.WriteAsync(entries, ct);
            throw new NotSupportedException("Simulated failure after writing entries");
        }
    }

    private sealed class ThrowAfterTurnoversWriter(IAccountingTurnoverWriter inner) : IAccountingTurnoverWriter
    {
        public Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default)
            => inner.DeleteForPeriodAsync(period, ct);

        public async Task WriteAsync(IEnumerable<NGB.Accounting.Turnovers.AccountingTurnover> turnovers, CancellationToken ct = default)
        {
            await inner.WriteAsync(turnovers, ct);
            throw new NotSupportedException("Simulated failure after writing turnovers");
        }
    }
}
