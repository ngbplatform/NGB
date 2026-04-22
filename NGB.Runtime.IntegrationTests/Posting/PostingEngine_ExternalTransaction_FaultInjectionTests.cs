using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_ExternalTransaction_FaultInjectionTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_ExternalTransaction_WhenEntryWriterThrowsAfterWriting_CallerRollback_RemovesAllSideEffects()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => ReplaceAccountingEntryWriterWithThrowAfterWrite(services));

        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var period = AccountingPeriod.FromDateTime(periodUtc);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            uow.HasActiveTransaction.Should().BeTrue();

            Func<Task> act = () => posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("Simulated failure after writing entries*");

            // PostingEngine must NOT rollback/commit in external transaction mode.
            uow.HasActiveTransaction.Should().BeTrue();

            await uow.RollbackAsync(CancellationToken.None);
        }

        // Assert
        await AssertNoSideEffectsAsync(host, documentId, period, PostingOperation.Post);
    }

    [Fact]
    public async Task PostAsync_ExternalTransaction_WhenTurnoverWriterThrowsAfterWriting_CallerRollback_RemovesAllSideEffects()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => ReplaceAccountingTurnoverWriterWithThrowAfterWrite(services));

        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc);
        var period = AccountingPeriod.FromDateTime(periodUtc);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            uow.HasActiveTransaction.Should().BeTrue();

            Func<Task> act = () => posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("50"), chart.Get("90.1"), 100m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("Simulated failure after writing turnovers*");

            uow.HasActiveTransaction.Should().BeTrue();
            await uow.RollbackAsync(CancellationToken.None);
        }

        // Assert
        await AssertNoSideEffectsAsync(host, documentId, period, PostingOperation.Post);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        // Cash
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        // Revenue
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);
    }

    private static async Task AssertNoSideEffectsAsync(IHost host, Guid documentId, DateOnly period, PostingOperation operation)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
        entries.Should().BeEmpty();

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(period, CancellationToken.None);
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

        page.Records.Should().BeEmpty("external transaction rollback must revert posting_log as well");
    }

    private static void ReplaceAccountingEntryWriterWithThrowAfterWrite(IServiceCollection services)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(IAccountingEntryWriter));
        services.Remove(descriptor);

        services.AddScoped<PostgresAccountingEntryWriter>();
        services.AddScoped<IAccountingEntryWriter>(sp =>
            new ThrowAfterEntriesWriter(sp.GetRequiredService<PostgresAccountingEntryWriter>()));
    }

    private static void ReplaceAccountingTurnoverWriterWithThrowAfterWrite(IServiceCollection services)
    {
        var descriptor = services.Single(d => d.ServiceType == typeof(IAccountingTurnoverWriter));
        services.Remove(descriptor);

        services.AddScoped<PostgresAccountingTurnoverWriter>();
        services.AddScoped<IAccountingTurnoverWriter>(sp =>
            new ThrowAfterTurnoversWriter(sp.GetRequiredService<PostgresAccountingTurnoverWriter>()));
    }

    private sealed class ThrowAfterEntriesWriter(IAccountingEntryWriter inner) : IAccountingEntryWriter
    {
        public async Task WriteAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct = default)
        {
            await inner.WriteAsync(entries, ct);
            throw new NotSupportedException("Simulated failure after writing entries");
        }
    }

    private sealed class ThrowAfterTurnoversWriter(IAccountingTurnoverWriter inner) : IAccountingTurnoverWriter
    {
        public Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default)
            => inner.DeleteForPeriodAsync(period, ct);

        public async Task WriteAsync(IEnumerable<AccountingTurnover> turnovers, CancellationToken ct = default)
        {
            await inner.WriteAsync(turnovers, ct);
            throw new NotSupportedException("Simulated failure after writing turnovers");
        }
    }
}
