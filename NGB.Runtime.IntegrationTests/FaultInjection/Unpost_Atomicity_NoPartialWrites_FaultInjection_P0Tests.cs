using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Posting;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

[Collection(PostgresCollection.Name)]
public sealed class Unpost_Atomicity_NoPartialWrites_FaultInjection_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task UnpostAsync_WhenEntryWriterFails_RollsBack_NoStornoEntries_NoUnpostPostingLog()
    {
        await Fixture.ResetDatabaseAsync();

        var period = new DateOnly(2038, 1, 1);
        var dayUtc = new DateTime(2038, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        using var goodHost = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(goodHost);

        var documentId = Guid.CreateVersion7();
        await PostAsync(goodHost, documentId, dayUtc, debit: "50", credit: "90.1", amount: 10m);

        using var badHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => { services.Decorate<IAccountingEntryWriter, ThrowAfterWriteEntryWriter>(); });

        // Act
        Func<Task> act = async () => await UnpostAsync(badHost, documentId);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Simulated entry writer failure*");

        await using (var scope = goodHost.Services.CreateAsyncScope())
        {
            var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
            var turnoverReader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
            var postingLogReader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);
            entries.Single().IsStorno.Should().BeFalse();

            // Turnovers must still reflect the original post.
            var turnovers = await turnoverReader.GetForPeriodAsync(period, CancellationToken.None);
            turnovers.Should().NotBeEmpty();

            // Unpost operation must NOT be recorded as completed.
            var unpostLogCount = await CountPostingLogRowsAsync(postingLogReader, documentId, PostingOperation.Unpost);
            unpostLogCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task UnpostAsync_WhenPostingLogTryBeginThrows_RollsBack_NoStornoEntries_NoPostingLogRow()
    {
        await Fixture.ResetDatabaseAsync();

        var period = new DateOnly(2038, 2, 1);
        var dayUtc = new DateTime(2038, 2, 5, 0, 0, 0, DateTimeKind.Utc);

        using var goodHost = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(goodHost);

        var documentId = Guid.CreateVersion7();
        await PostAsync(goodHost, documentId, dayUtc, debit: "50", credit: "90.1", amount: 10m);

        using var badHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => { services.Decorate<IPostingStateRepository, ThrowAfterTryBeginPostingLogRepository>(); });

        // Act
        Func<Task> act = async () => await UnpostAsync(badHost, documentId);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Simulated posting log TryBegin failure*");

        await using (var scope = goodHost.Services.CreateAsyncScope())
        {
            var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
            var turnoverReader = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
            var postingLogReader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);
            entries.Single().IsStorno.Should().BeFalse();

            var turnovers = await turnoverReader.GetForPeriodAsync(period, CancellationToken.None);
            turnovers.Should().NotBeEmpty();

            var unpostLogCount = await CountPostingLogRowsAsync(postingLogReader, documentId, PostingOperation.Unpost);
            unpostLogCount.Should().Be(0);
        }
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debit,
        string credit,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get(debit), chart.Get(credit), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var unposting = scope.ServiceProvider.GetRequiredService<UnpostingService>();
        await unposting.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task<int> CountPostingLogRowsAsync(
        IPostingStateReader reader,
        Guid documentId,
        PostingOperation operation)
    {
        var page = await reader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddHours(-1),
            ToUtc = DateTime.UtcNow.AddHours(1),
            DocumentId = documentId,
            Operation = operation,
            PageSize = 50
        }, CancellationToken.None);

        return page.Records.Count;
    }

    private sealed class ThrowAfterWriteEntryWriter(IAccountingEntryWriter inner) : IAccountingEntryWriter
    {
        public async Task WriteAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct = default)
        {
            await inner.WriteAsync(entries, ct);
            throw new NotSupportedException("Simulated entry writer failure");
        }
    }

    private sealed class ThrowAfterTryBeginPostingLogRepository(IPostingStateRepository inner) : IPostingStateRepository
    {
        public async Task<PostingStateBeginResult> TryBeginAsync(
            Guid documentId,
            PostingOperation operation,
            DateTime startedAtUtc,
            CancellationToken ct = default)
        {
            _ = await inner.TryBeginAsync(documentId, operation, startedAtUtc, ct);
            throw new NotSupportedException("Simulated posting log TryBegin failure");
        }

        public Task MarkCompletedAsync(Guid documentId, PostingOperation operation, DateTime completedAtUtc, CancellationToken ct = default) =>
            inner.MarkCompletedAsync(documentId, operation, completedAtUtc, ct);

        public Task ClearCompletedStateAsync(
            Guid documentId,
            PostingOperation operation,
            CancellationToken ct = default) =>
            inner.ClearCompletedStateAsync(documentId, operation, ct);
    }
}
