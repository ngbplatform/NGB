using System.Data.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Accounts;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

/// <summary>
/// P4 / Extreme edge-case:
/// If CommitAsync fails after all writes (register + turnovers + posting_log.MarkCompleted),
/// the transaction must still be rolled back and NO partial state must remain.
/// A subsequent retry must be able to post successfully (no "InProgress" poisoning).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingCommitFailure_RollbackAndRetry_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CommitFailure_RollsBackEverything_AndRetrySucceeds()
    {
        // Arrange
        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 01, 15, 0, 0, 0, DateTimeKind.Utc);

        using var failingCommitHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                // A per-test gate so we can fail only the *posting* commit, not the setup commits.
                services.AddSingleton<ICommitFailureGate, CommitFailureGate>();

                // Replace IUnitOfWork with a wrapper that throws on CommitAsync.
                services.RemoveAll<IUnitOfWork>();
                services.AddScoped<IUnitOfWork>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
                    var logger = sp.GetRequiredService<ILogger<PostgresUnitOfWork>>();
                    var gate = sp.GetRequiredService<ICommitFailureGate>();
                    var inner = new PostgresUnitOfWork(options.ConnectionString, logger);
                    return new ThrowOnCommitUnitOfWork(inner, gate);
                });
            });

        await EnsureMinimalAccountsAsync(failingCommitHost);

        // Act 1: attempt posting; commit fails
        var act = async () =>
        {
            await using var scope = failingCommitHost.Services.CreateAsyncScope();

            // Fail the next CommitAsync in THIS scope.
            scope.ServiceProvider.GetRequiredService<ICommitFailureGate>().FailNextCommit();

            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var cash = chart.Get("50");
                    var revenue = chart.Get("90.1");

                    ctx.Post(documentId, period, cash, revenue, 10m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        };

        await act.Should().ThrowAsync<SimulatedCommitFailureException>();

        // Assert: DB has no partial writes (everything must be rolled back)
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var regCount = await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", documentId) }
            }.ExecuteScalarAsync();

            var logCount = await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_posting_state WHERE document_id = @d AND operation = @op",
                conn)
            {
                Parameters =
                {
                    new("d", documentId),
                    new("op", (short)PostingOperation.Post)
                }
            }.ExecuteScalarAsync();

            ((int)regCount!).Should().Be(0, "commit failure must not leave any register rows");
            ((int)logCount!).Should().Be(0, "commit failure must not leave posting_log rows");
        }

        // Act 2: retry with normal host => must succeed
        using var normalHost = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await EnsureMinimalAccountsAsync(normalHost);

        await using (var scope = normalHost.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            var result = await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var cash = chart.Get("50");
                    var revenue = chart.Get("90.1");

                    ctx.Post(documentId, period, cash, revenue, 10m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);

            result.Should().Be(PostingResult.Executed);
        }

        // Assert: now state exists and posting_log is completed
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var regCount = await new NpgsqlCommand(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d",
                conn)
            {
                Parameters = { new("d", documentId) }
            }.ExecuteScalarAsync();

            var completedAt = await new NpgsqlCommand(
                "SELECT completed_at_utc FROM accounting_posting_state WHERE document_id = @d AND operation = @op",
                conn)
            {
                Parameters =
                {
                    new("d", documentId),
                    new("op", (short)PostingOperation.Post)
                }
            }.ExecuteScalarAsync();

            ((int)regCount!).Should().Be(1);
            completedAt.Should().NotBeNull("successful retry must complete posting_log");
        }
    }

    private static async Task EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        // The fixture/demo may already seed a minimal chart of accounts. Make this helper idempotent.
        // Unique constraint is for "not deleted" rows, so we only skip when the existing row is not deleted.
        var existing = await repo.GetForAdminAsync(includeDeleted: true, ct: CancellationToken.None);
        static bool HasNotDeleted(IReadOnlyList<ChartOfAccountsAdminItem> items, string code) =>
            items.Any(x => !x.IsDeleted && string.Equals(x.Account.Code, code, StringComparison.OrdinalIgnoreCase));

        // NOTE: all NegativeBalancePolicy are Allow here, the test is about transaction integrity, not balances.
        if (!HasNotDeleted(existing, "50"))
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        if (!HasNotDeleted(existing, "90.1"))
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "90.1",
                Name: "Revenue",
                AccountType.Income,
                StatementSection: StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }
    }

    private sealed class SimulatedCommitFailureException : Exception
    {
        public SimulatedCommitFailureException() : base("Simulated CommitAsync failure") { }
    }

    private interface ICommitFailureGate
    {
        void FailNextCommit();
        bool ShouldFailCommitAndConsume();
    }

    private sealed class CommitFailureGate : ICommitFailureGate
    {
        private readonly AsyncLocal<bool> _failNext = new();

        public void FailNextCommit() => _failNext.Value = true;

        public bool ShouldFailCommitAndConsume()
        {
            if (_failNext.Value)
            {
                _failNext.Value = false;
                return true;
            }

            return false;
        }
    }

    private sealed class ThrowOnCommitUnitOfWork(IUnitOfWork inner, ICommitFailureGate gate) : IUnitOfWork
    {
        public DbConnection Connection => inner.Connection;
        public DbTransaction? Transaction => inner.Transaction;
        public bool HasActiveTransaction => inner.HasActiveTransaction;

        public Task EnsureConnectionOpenAsync(CancellationToken ct = default) => inner.EnsureConnectionOpenAsync(ct);

        public Task BeginTransactionAsync(CancellationToken ct = default) => inner.BeginTransactionAsync(ct);

        public Task CommitAsync(CancellationToken ct = default)
            => gate.ShouldFailCommitAndConsume()
                ? throw new SimulatedCommitFailureException()
                : inner.CommitAsync(ct);

        public Task RollbackAsync(CancellationToken ct = default) => inner.RollbackAsync(ct);

        public void EnsureActiveTransaction() => inner.EnsureActiveTransaction();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
