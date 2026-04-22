using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Accounting.PostingState;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class PostingEngine_TurnoverWriter_BatchSplit_Over5kKeys_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // Turnover writer batches at 5_000. We need > 5_000 unique turnover keys.
    // With the "simple" DimensionSetId strategy (Guid.Empty for all entries), a turnover key is:
    //   (period_month, account_id, dimension_set_id)
    // so we generate 5_200 unique account_ids to produce 5_200 unique turnover keys.
    // Each entry contributes TWO turnover keys (DT + CT), so 2_600 entries => 5_200 keys.
    private const int Entries = 2_600;

    [Fact]
    public async Task PostAsync_Over5kTurnoverKeys_WritesAllTurnovers()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedAccountsFastAsync(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var postingDayUtc = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);
        var periodMonth = AccountingPeriod.FromDateTime(postingDayUtc);

        // Act
        await using (var scopePosting = host.Services.CreateAsyncScope())
        {
            var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                operation: PostingOperation.Post,
                postingAction: async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    // We intentionally keep entries < 5k to avoid entry writer batching,
                    // while forcing turnover writer batching by producing > 5k unique turnover keys.
                    for (var i = 0; i < Entries; i++)
                    {
                        // Two unique accounts per entry => 5_200 unique turnover keys.
                        var debit = chart.Get($"D{i:D4}");
                        var credit = chart.Get($"C{i:D4}");

                        ctx.Post(
                            documentId,
                            postingDayUtc,
                            debit,
                            credit,
                            amount: 1m);
                    }
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        // Assert (direct SQL; stress-friendly)
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var total = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_turnovers WHERE period = @period;",
            new { period = periodMonth });

        total.Should().Be(Entries * 2);

        var distinctSets = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT dimension_set_id) FROM accounting_turnovers WHERE period = @period;",
            new { period = periodMonth });

        distinctSets.Should().Be(1);

        var onlySet = await conn.ExecuteScalarAsync<Guid>(
            "SELECT dimension_set_id FROM accounting_turnovers WHERE period = @period LIMIT 1;",
            new { period = periodMonth });

        onlySet.Should().Be(Guid.Empty);

        var debitRows = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_turnovers WHERE period = @period AND debit_amount > 0;",
            new { period = periodMonth });

        debitRows.Should().Be(Entries);

        var creditRows = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_turnovers WHERE period = @period AND credit_amount > 0;",
            new { period = periodMonth });

        creditRows.Should().Be(Entries);

        var bothSides = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_turnovers WHERE period = @period AND debit_amount > 0 AND credit_amount > 0;",
            new { period = periodMonth });

        bothSides.Should().Be(0);

        var totalDebit = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(debit_amount), 0) FROM accounting_turnovers WHERE period = @period;",
            new { period = periodMonth });

        totalDebit.Should().Be(Entries);

        var totalCredit = await conn.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(credit_amount), 0) FROM accounting_turnovers WHERE period = @period;",
            new { period = periodMonth });

        totalCredit.Should().Be(Entries);
    }

    private static async Task SeedAccountsFastAsync(string connectionString)
    {
        // This test is intentionally heavy; use a single bulk insert for speed.
        // Account codes are unique and trimmed, so they satisfy code_norm uniqueness.

        var count = Entries * 2;
        var ids = new Guid[count];
        var codes = new string[count];
        var names = new string[count];
        var types = new short[count];
        var sections = new short[count];
        var policies = new short[count];

        for (var i = 0; i < Entries; i++)
        {
            // Debit accounts (Assets)
            ids[i] = Guid.CreateVersion7();
            codes[i] = $"D{i:D4}";
            names[i] = $"Debit {i}";
            types[i] = (short)AccountType.Asset;
            sections[i] = (short)StatementSection.Assets;
            policies[i] = (short)NegativeBalancePolicy.Allow;

            // Credit accounts (Income)
            var j = i + Entries;
            ids[j] = Guid.CreateVersion7();
            codes[j] = $"C{i:D4}";
            names[j] = $"Credit {i}";
            types[j] = (short)AccountType.Income;
            sections[j] = (short)StatementSection.Income;
            policies[j] = (short)NegativeBalancePolicy.Allow;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = """
                           INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
                           SELECT *
                           FROM UNNEST(
                               @ids::uuid[],
                               @codes::text[],
                               @names::text[],
                               @types::smallint[],
                               @sections::smallint[],
                               @policies::smallint[]
                           );
                           """;

        await conn.ExecuteAsync(sql, new { ids, codes, names, types, sections, policies });
    }
}
