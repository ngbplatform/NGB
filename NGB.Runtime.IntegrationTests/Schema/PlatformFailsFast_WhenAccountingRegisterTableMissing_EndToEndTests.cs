using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

/// <summary>
/// P3 coverage: if the DB schema is broken (e.g., register table missing), platform must fail fast.
/// IMPORTANT: This test must be schema-safe (must restore the table name), otherwise it will break Respawn for all tests.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PlatformFailsFast_WhenAccountingRegisterTableMissing_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Posting_WhenAccountingRegisterMainTableMissing_ThrowsPostgresUndefinedTable()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Ensure we have a minimal CoA so PostingAction reaches the DB write layer.
        await SeedMinimalCoaAsync(host);

        const string table = "accounting_register_main";

        // NOTE: to_regclass returns type regclass; cast to text to avoid Npgsql trying to read it as "object".
        var exists = await ExecuteScalarStringAsync($"select to_regclass('public.{table}')::text;");
        exists.Should().NotBeNull("migration must create {0} table for this platform", table);

        var bak = $"{table}__bak_{Guid.CreateVersion7():N}";

        try
        {
            await ExecuteNonQueryAsync($"alter table public.{table} rename to {bak};");

            // Now any posting should fail with undefined_table for the missing register main table.
            var docId = Guid.CreateVersion7();
            var period = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);

            Func<Task> act = async () =>
            {
                await using var scope = host.Services.CreateAsyncScope();
                var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

                await posting.PostAsync(PostingOperation.Post, async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);

                    // Minimal posting action: it will attempt to write to the register main table.
                    ctx.Post(docId, period, chart.Get("50"), chart.Get("90.1"), 1m);
                }, manageTransaction: true, CancellationToken.None);
            };

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("42P01");
            ex.Which.MessageText.Should().Contain(table);
        }
        finally
        {
            // Always restore schema so Respawn and other tests keep working.
            await ExecuteNonQueryAsync($"alter table public.{bak} rename to {table};");
        }
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Fresh DB per test, so we can create without "if exists" guards.
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

    private async Task ExecuteNonQueryAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private async Task<string?> ExecuteScalarStringAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);

        var value = await cmd.ExecuteScalarAsync(CancellationToken.None);

        return value is null or DBNull ? null : (string)value;
    }
}
