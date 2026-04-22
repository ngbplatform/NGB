using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingLog_Takeover_InProgressTimeout_EndToEndTests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    // Align with runtime constant (TimeSpan.FromMinutes(10)) but keep a safe margin.
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task PostAsync_WhenInProgressIsNotStale_Rejects_AndDoesNotWriteAnything()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        var period = new DateTime(2026, 1, 13, 0, 0, 0, DateTimeKind.Utc);
        var documentId = Guid.CreateVersion7();

        await SeedMinimalCoaAsync(host);

        // Simulate another transaction already started posting recently (NOT stale).
        var startedAtUtc = DateTime.UtcNow - (InProgressTimeout - TimeSpan.FromSeconds(5));
        await InsertInProgressPostingLogRowAsync(
            fixture.ConnectionString,
            documentId,
            PostingOperation.Post,
            startedAtUtc);

        // Act
        Func<Task> act = () => PostOnceAsync(host, documentId, period, amount: 100m);

        // Assert
        await act.Should().ThrowAsync<PostingAlreadyInProgressException>()
            .WithMessage("*already in progress*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None)).Should().BeEmpty();
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Revenue,
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid documentId, DateTime period, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();
        await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, period, chart.Get(Cash), chart.Get(Revenue), amount);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
    }
    private static async Task InsertInProgressPostingLogRowAsync(
        string connectionString,
        Guid documentId,
        PostingOperation operation,
        DateTime startedAtUtc)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
INSERT INTO accounting_posting_state (document_id, operation, started_at_utc, completed_at_utc)
VALUES (@document_id, @operation, @started_at_utc, NULL);";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("operation", (byte)operation);
        cmd.Parameters.AddWithValue("started_at_utc", startedAtUtc);

        await cmd.ExecuteNonQueryAsync();
    }
}
