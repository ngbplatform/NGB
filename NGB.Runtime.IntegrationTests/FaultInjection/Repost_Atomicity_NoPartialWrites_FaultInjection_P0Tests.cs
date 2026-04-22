using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Turnovers;
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
public sealed class Repost_Atomicity_NoPartialWrites_FaultInjection_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RepostAsync_WhenTurnoverWriterFails_RollsBack_NoStorno_NoRepostPostingLog_AndRetrySucceeds()
    {
        await Fixture.ResetDatabaseAsync();

        var dayUtc = new DateTime(2038, 3, 10, 0, 0, 0, DateTimeKind.Utc);

        // First, create the baseline state using a "good" host.
        using var goodHost = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedMinimalCoaAsync(goodHost);

        var documentId = Guid.CreateVersion7();
        await PostAsync(goodHost, documentId, dayUtc, debit: "50", credit: "90.1", amount: 100m);

        // Now attempt repost with a turnover-writer that throws AFTER writing.
        using var badHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => { services.Decorate<IAccountingTurnoverWriter, ThrowAfterWriteTurnoverWriter>(); });

        // Act
        Func<Task> act = async () =>
        {
            await using var scope = badHost.Services.CreateAsyncScope();
            var reposting = scope.ServiceProvider.GetRequiredService<RepostingService>();

            await reposting.RepostAsync(
                documentId,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, dayUtc, chart.Get("50"), chart.Get("90.1"), 200m);
                },
                CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Simulated turnover writer failure*");

        await using (var scope = goodHost.Services.CreateAsyncScope())
        {
            var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
            var postingLogReader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(1);
            entries.Single().IsStorno.Should().BeFalse("failed repost must not persist storno or new entries");

            var repostLogCount = await CountPostingLogRowsAsync(postingLogReader, documentId, PostingOperation.Repost);
            repostLogCount.Should().Be(0);
        }

        // Retry with a normal host (same DB)
        using var retryHost = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using (var scope = retryHost.Services.CreateAsyncScope())
        {
            var reposting = scope.ServiceProvider.GetRequiredService<RepostingService>();

            await reposting.RepostAsync(
                documentId,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, dayUtc, chart.Get("50"), chart.Get("90.1"), 200m);
                },
                CancellationToken.None);
        }

        await using (var scope = retryHost.Services.CreateAsyncScope())
        {
            var entryReader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
            var postingLogReader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

            var entries = await entryReader.GetByDocumentAsync(documentId, CancellationToken.None);
            entries.Should().HaveCount(3, "old + storno + new");
            entries.Count(e => e.IsStorno).Should().Be(1);

            var repostLogCount = await CountPostingLogRowsAsync(postingLogReader, documentId, PostingOperation.Repost);
            repostLogCount.Should().Be(1);
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
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get(debit), chart.Get(credit), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
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

    private sealed class ThrowAfterWriteTurnoverWriter(IAccountingTurnoverWriter inner) : IAccountingTurnoverWriter
    {
        public Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default)
            => inner.DeleteForPeriodAsync(period, ct);

        public async Task WriteAsync(IEnumerable<AccountingTurnover> turnovers, CancellationToken ct = default)
        {
            await inner.WriteAsync(turnovers, ct);
            throw new NotSupportedException("Simulated turnover writer failure");
        }
    }
}
