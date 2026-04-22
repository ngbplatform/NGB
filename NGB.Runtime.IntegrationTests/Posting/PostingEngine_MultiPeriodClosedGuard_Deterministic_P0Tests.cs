using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_MultiPeriodClosedGuard_Deterministic_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostingAcrossTwoMonths_WhenSecondMonthClosed_ThrowsWithExactMonthStart_AndWritesNothing()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = CreateHostWithRelaxedPostingValidator();
        await SeedMinimalCoaAsync(host);

        var logWindow = PostingLogTestWindow.Capture();

        var jan = new DateOnly(2026, 1, 1);
        var feb = new DateOnly(2026, 2, 1);

        await MarkPeriodClosedAsync(feb);

        var docId = Guid.CreateVersion7();

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                // Intentionally in reverse (Feb first, then Jan): engine must still check in deterministic order.
                ctx.Post(docId, new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), chart.Get("50"), chart.Get("90.1"), 1m);
                ctx.Post(docId, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), chart.Get("50"), chart.Get("90.1"), 1m);
            }, manageTransaction: true, ct: CancellationToken.None);
        };

        (await act.Should().ThrowAsync<PostingPeriodClosedException>())
            .Which.Message.Should().Contain($"{feb:yyyy-MM-dd}");

        await AssertNoWritesAsync(host, docId, jan, feb, logWindow);
    }

    [Fact]
    public async Task PostingAcrossTwoClosedMonths_ThrowsForEarliestClosedMonth_Deterministically_AndWritesNothing()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = CreateHostWithRelaxedPostingValidator();
        await SeedMinimalCoaAsync(host);

        var logWindow = PostingLogTestWindow.Capture();

        var jan = new DateOnly(2026, 1, 1);
        var feb = new DateOnly(2026, 2, 1);

        await MarkPeriodClosedAsync(feb);
        await MarkPeriodClosedAsync(jan);

        var docId = Guid.CreateVersion7();

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                ctx.Post(docId, new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), chart.Get("50"), chart.Get("90.1"), 1m);
                ctx.Post(docId, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), chart.Get("50"), chart.Get("90.1"), 1m);
            }, manageTransaction: true, ct: CancellationToken.None);
        };

        (await act.Should().ThrowAsync<PostingPeriodClosedException>())
            .Which.Message.Should().Contain($"{jan:yyyy-MM-dd}");

        await AssertNoWritesAsync(host, docId, jan, feb, logWindow);
    }

    private IHost CreateHostWithRelaxedPostingValidator()
        => IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<IAccountingPostingValidator>();
            services.AddScoped<IAccountingPostingValidator, RelaxedAccountingPostingValidator>();
        });

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow), CancellationToken.None);
    }

    private async Task MarkPeriodClosedAsync(DateOnly period)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        const string sql = """
            INSERT INTO accounting_closed_periods (period, closed_by, closed_at_utc)
            VALUES (@p, 'it', @at)
            ON CONFLICT (period) DO NOTHING;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("p", period);
        cmd.Parameters.AddWithValue("at", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task AssertNoWritesAsync(IHost host, Guid documentId, DateOnly period1, DateOnly period2, PostingLogTestWindow logWindow)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var entries = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();
        var turnovers = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>();
        var log = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

        (await entries.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();
        (await turnovers.GetForPeriodAsync(period1, CancellationToken.None)).Should().BeEmpty();
        (await turnovers.GetForPeriodAsync(period2, CancellationToken.None)).Should().BeEmpty();

        var page = await log.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = logWindow.FromUtc,
            ToUtc = logWindow.ToUtc,
            DocumentId = documentId,
            PageSize = 10_000,
        }, CancellationToken.None);

        page.Records.Should().BeEmpty();
    }
}
