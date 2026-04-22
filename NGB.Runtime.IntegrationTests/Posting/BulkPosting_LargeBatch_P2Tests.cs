using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
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
public sealed class BulkPosting_LargeBatch_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task PostAsync_LargeBatch_10kEntries_WritesAllRegisterRows_AndAggregatesTurnovers()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var postingDayUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var month = AccountingPeriod.FromDateTime(postingDayUtc);

        const int n = 10_000;

        // Act
        await using (var scopePosting = host.Services.CreateAsyncScope())
        {
            var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var cash = chart.Get(Cash);
                    var revenue = chart.Get(Revenue);

                    // 10k uniform entries. We intentionally avoid fetching entries back through the reader
                    // (which returns the whole list) and instead verify counts & aggregated results.
                    for (var i = 0; i < n; i++)
                    {
                        ctx.Post(documentId, postingDayUtc, cash, revenue, 1m);
                    }
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Register row count (direct SQL, stress-friendly).
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @documentId;",
                new { documentId });

            count.Should().Be(n);
        }

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        var turnovers = await turnoverReader.GetForPeriodAsync(month, CancellationToken.None);

        turnovers.Should().HaveCount(2);

        var cashTurnover = turnovers.Should().ContainSingle(x => x.AccountCode == Cash).Which;
        cashTurnover.DebitAmount.Should().Be(n);
        cashTurnover.CreditAmount.Should().Be(0m);

        var revenueTurnover = turnovers.Should().ContainSingle(x => x.AccountCode == Revenue).Which;
        revenueTurnover.DebitAmount.Should().Be(0m);
        revenueTurnover.CreditAmount.Should().Be(n);

        // Posting log must contain exactly one Completed record for this document.
        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var page = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UtcNow.AddDays(-7),
            ToUtc = DateTime.UtcNow.AddDays(7),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10
        }, CancellationToken.None);

        page.Records.Should().HaveCount(1);
        page.Records[0].Status.Should().Be(PostingStateStatus.Completed);
        page.Records[0].CompletedAtUtc.Should().NotBeNull();
    }

    private static async Task SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Cash,
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: Revenue,
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }
}
