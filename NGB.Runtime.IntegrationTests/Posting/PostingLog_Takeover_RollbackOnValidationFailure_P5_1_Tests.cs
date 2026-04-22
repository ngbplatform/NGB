using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingLog_Takeover_RollbackOnValidationFailure_P5_1_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task PostAsync_StaleInProgress_TakeoverUpdate_IsRolledBack_WhenValidatorThrows_ManagedTransaction()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 17, 12, 0, 0, DateTimeKind.Utc);

        var staleStartedAtUtc = DateTime.UtcNow.AddHours(-2);
        await InsertInProgressPostingLogRowAsync(
            Fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc: staleStartedAtUtc);

        var seeded = await GetPostingLogRowAsync(Fixture.ConnectionString, documentId, PostingOperation.Post);
        seeded.startedAtUtc.Should().Be(staleStartedAtUtc);
        seeded.completedAtUtc.Should().BeNull();

        // Act: takeover should happen inside the transaction, but validator throws -> rollback.
        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);

                    // Amount=0 triggers BasicAccountingPostingValidator after posting_log takeover update.
                    ctx.Post(documentId, periodUtc, chart.Get(Cash), chart.Get(Revenue), 0m);
                },
                ct: CancellationToken.None);
        };

        await act.Should().ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*must be > 0*DocumentId=*");

        // Assert: posting_log row must remain unchanged.
        var row = await GetPostingLogRowAsync(Fixture.ConnectionString, documentId, PostingOperation.Post);
        row.startedAtUtc.Should().Be(staleStartedAtUtc, "takeover update must be rolled back on validator failure");
        row.completedAtUtc.Should().BeNull();

        // And no writes occurred.
        (await CountRegisterRowsByDocumentAsync(Fixture.ConnectionString, documentId)).Should().Be(0);
    }

    [Fact]
    public async Task PostAsync_StaleInProgress_TakeoverUpdate_IsRolledBack_WhenValidatorThrows_ExternalTransaction()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await SeedMinimalCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 17, 13, 0, 0, DateTimeKind.Utc);

        var staleStartedAtUtc = DateTime.UtcNow.AddHours(-2);
        await InsertInProgressPostingLogRowAsync(
            Fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc: staleStartedAtUtc);

        // Act: external transaction mode, validator throws, caller rolls back.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var posting = sp.GetRequiredService<PostingEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            Func<Task> act = () => posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get(Cash), chart.Get(Revenue), 0m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NgbArgumentInvalidException>()
                .WithMessage("*must be > 0*DocumentId=*");

            await uow.RollbackAsync(CancellationToken.None);
        }

        // Assert: posting_log row must remain unchanged.
        var row = await GetPostingLogRowAsync(Fixture.ConnectionString, documentId, PostingOperation.Post);
        row.startedAtUtc.Should().Be(staleStartedAtUtc);
        row.completedAtUtc.Should().BeNull();

        (await CountRegisterRowsByDocumentAsync(Fixture.ConnectionString, documentId)).Should().Be(0);
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Cash,
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Revenue,
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task InsertInProgressPostingLogRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = """
                           INSERT INTO accounting_posting_state(
                               document_id, operation, started_at_utc, completed_at_utc
                           )
                           VALUES (@document_id, @operation, @started_at_utc, NULL);
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);
        cmd.Parameters.AddWithValue("started_at_utc", startedAtUtc);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(DateTime startedAtUtc, DateTime? completedAtUtc)> GetPostingLogRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = """
                           SELECT started_at_utc, completed_at_utc
                           FROM accounting_posting_state
                           WHERE document_id = @document_id AND operation = @operation;
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (short)operation);

        await using var reader = await cmd.ExecuteReaderAsync();
        reader.Read().Should().BeTrue("posting_log seed row must exist");
        var started = reader.GetDateTime(0);
        var completed = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
        return (started, completed);
    }

    private static async Task<int> CountRegisterRowsByDocumentAsync(string connectionString, Guid documentId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = """
                           SELECT COUNT(*)
                           FROM accounting_register_main
                           WHERE document_id = @document_id;
                           """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
