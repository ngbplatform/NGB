using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Accounts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

/// <summary>
/// P4 / Extreme edge-case:
/// In manageTransaction=false mode PostingEngine must not commit or rollback.
/// The caller controls the transaction boundary.
/// This test verifies that a caller rollback removes ALL side effects,
/// even if PostingEngine returned Executed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingExternalTransactionMode_RollbackSemantics_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ExternalTransaction_Rollback_RemovesRegisterAndPostingLog()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await EnsureMinimalAccountsAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 01, 15, 0, 0, 0, DateTimeKind.Utc);

        // Act: post inside an external transaction and then rollback
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var result = await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var cash = chart.Get("50");
                    var revenue = chart.Get("90.1");
                    ctx.Post(documentId, period, cash, revenue, 10m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            result.Should().Be(PostingResult.Executed,
                "PostingEngine does its work inside the transaction, but does not commit in external mode");

            await uow.RollbackAsync(CancellationToken.None);
        }

        // Assert: rollback removed all side effects
        await AssertNoRegisterAndNoLogAsync(documentId);

        // Act 2: now commit to ensure the happy-path works in external mode
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var result = await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var cash = chart.Get("50");
                    var revenue = chart.Get("90.1");
                    ctx.Post(documentId, period, cash, revenue, 10m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            result.Should().Be(PostingResult.Executed);

            await uow.CommitAsync(CancellationToken.None);
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
}
