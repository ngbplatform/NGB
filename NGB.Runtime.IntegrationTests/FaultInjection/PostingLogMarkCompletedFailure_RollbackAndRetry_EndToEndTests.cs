using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Accounts;
using NGB.Persistence.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.PostingState;
using NGB.PostgreSql.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

/// <summary>
/// P4 / Extreme edge-case:
/// If posting_log.MarkCompletedAsync fails after all writes (register + turnovers),
/// the transaction must be rolled back and leave NO partial state behind.
/// A subsequent retry must succeed (no "InProgress" poisoning).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingLogMarkCompletedFailure_RollbackAndRetry_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MarkCompletedFailure_RollsBackEverything_AndRetrySucceeds()
    {
        // Arrange
        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 01, 15, 0, 0, 0, DateTimeKind.Utc);

        using var failingHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                // Fail MarkCompleted once, but only in the posting scope.
                services.AddSingleton<IMarkCompletedFailureGate, MarkCompletedFailureGate>();

                services.RemoveAll<IPostingStateRepository>();
                services.AddScoped<PostgresPostingStateRepository>();
                services.AddScoped<IPostingStateRepository>(sp =>
                    new ThrowOnMarkCompletedPostingLogRepository(
                        sp.GetRequiredService<PostgresPostingStateRepository>(),
                        sp.GetRequiredService<IMarkCompletedFailureGate>()));

                // Ensure unit-of-work is the real Postgres implementation.
                services.RemoveAll<IUnitOfWork>();
                services.AddScoped<IUnitOfWork>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
                    var logger = sp.GetRequiredService<ILogger<PostgresUnitOfWork>>();
                    return new PostgresUnitOfWork(options.ConnectionString, logger);
                });
            });

        await EnsureMinimalAccountsAsync(failingHost);

        // Act 1: MarkCompleted throws
        var act = async () =>
        {
            await using var scope = failingHost.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IMarkCompletedFailureGate>().FailNextMarkCompleted();

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

        await act.Should().ThrowAsync<SimulatedMarkCompletedFailureException>();

        // Assert: no partial writes
        await AssertNoRegisterAndNoLogAsync(documentId);

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

        await AssertRegisterExistsAndLogCompletedAsync(documentId);
    }

    private async Task AssertNoRegisterAndNoLogAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
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

        ((int)regCount!).Should().Be(0);
        ((int)logCount!).Should().Be(0);
    }

    private async Task AssertRegisterExistsAndLogCompletedAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
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
        completedAt.Should().NotBeNull();
    }

    private static async Task EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var existing = await repo.GetForAdminAsync(includeDeleted: true, ct: CancellationToken.None);
        static bool HasNotDeleted(IReadOnlyList<ChartOfAccountsAdminItem> items, string code) =>
            items.Any(x => !x.IsDeleted && string.Equals(x.Account.Code, code, StringComparison.OrdinalIgnoreCase));

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

    private sealed class SimulatedMarkCompletedFailureException : Exception
    {
        public SimulatedMarkCompletedFailureException() : base("Simulated MarkCompletedAsync failure") { }
    }

    private interface IMarkCompletedFailureGate
    {
        void FailNextMarkCompleted();
        bool ShouldFailAndConsume();
    }

    private sealed class MarkCompletedFailureGate : IMarkCompletedFailureGate
    {
        private readonly AsyncLocal<bool> _failNext = new();

        public void FailNextMarkCompleted() => _failNext.Value = true;

        public bool ShouldFailAndConsume()
        {
            if (_failNext.Value)
            {
                _failNext.Value = false;
                return true;
            }

            return false;
        }
    }

    private sealed class ThrowOnMarkCompletedPostingLogRepository(
        IPostingStateRepository inner,
        IMarkCompletedFailureGate gate) : IPostingStateRepository
    {
        public Task<PostingStateBeginResult> TryBeginAsync(
            Guid documentId,
            PostingOperation operation,
            DateTime startedAtUtc,
            CancellationToken ct = default)
            => inner.TryBeginAsync(documentId, operation, startedAtUtc, ct);

        public Task MarkCompletedAsync(
            Guid documentId,
            PostingOperation operation,
            DateTime completedAtUtc,
            CancellationToken ct = default)
            => gate.ShouldFailAndConsume()
                ? throw new SimulatedMarkCompletedFailureException()
                : inner.MarkCompletedAsync(documentId, operation, completedAtUtc, ct);

        public Task ClearCompletedStateAsync(
            Guid documentId,
            PostingOperation operation,
            CancellationToken ct = default)
            => inner.ClearCompletedStateAsync(documentId, operation, ct);
    }
}
