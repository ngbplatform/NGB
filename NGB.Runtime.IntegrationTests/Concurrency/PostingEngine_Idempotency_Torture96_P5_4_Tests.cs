using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using Npgsql;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_Idempotency_Torture96_P5_4_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostingEngine_Torture_96_Tasks_SameDocsAndOperations_IsIdempotent_AndLeavesConsistentDb()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var periodUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(2026, 1, 1);

        // 8 documents x 3 operations x 4 duplicates = 96 tasks.
        var docs = Enumerable.Range(0, 8).Select(_ => Guid.CreateVersion7()).ToArray();
        var operations = new[] { PostingOperation.Post, PostingOperation.Unpost, PostingOperation.Repost };
        const int duplicates = 4;

        var taskCount = docs.Length * operations.Length * duplicates;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = new List<Task>(taskCount);
        foreach (var docId in docs)
        {
            foreach (var op in operations)
            {
                for (var i = 0; i < duplicates; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await start.Task;

                        await using var scope = host.Services.CreateAsyncScope();
                        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

                        await posting.PostAsync(
                            operation: op,
                            postingAction: async (ctx, ct) =>
                            {
                                var chart = await ctx.GetChartOfAccountsAsync(ct);
                                ctx.Post(docId, periodUtc, chart.Get("50"), chart.Get("90.1"), 1m);
                            },
                            ct: CancellationToken.None);
                    }));
                }
            }
        }

        start.SetResult();

        await Task.WhenAll(tasks);

        // Assert: exactly one completed posting_log record per (doc, operation).
        (await CountPostingLogRowsAsync(Fixture.ConnectionString)).Should().Be(docs.Length * operations.Length);
        (await CountRegisterRowsAsync(Fixture.ConnectionString)).Should().Be(docs.Length * operations.Length);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var report = await scope.ServiceProvider.GetRequiredService<IAccountingConsistencyReportReader>()
                .RunForPeriodAsync(period, previousPeriodForChainCheck: null, CancellationToken.None);

            report.IsOk.Should().BeTrue();
            report.TurnoversVsRegisterDiffCount.Should().Be(0);
            report.Issues.Should().BeEmpty();

            var turnovers = await scope.ServiceProvider.GetRequiredService<IAccountingTurnoverReader>()
                .GetForPeriodAsync(period, CancellationToken.None);

            // Each (doc, operation) produces one balanced entry of amount 1.
            var expected = docs.Length * operations.Length * 1m;

            turnovers.Should().ContainSingle(t => t.AccountCode == "50" && t.DebitAmount == expected && t.CreditAmount == 0m);
            turnovers.Should().ContainSingle(t => t.AccountCode == "90.1" && t.CreditAmount == expected && t.DebitAmount == 0m);
        }
    }

    private static async Task<int> CountPostingLogRowsAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        const string sql = "SELECT COUNT(*) FROM accounting_posting_state WHERE completed_at_utc IS NOT NULL;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<int> CountRegisterRowsAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        const string sql = "SELECT COUNT(*) FROM accounting_register_main;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
