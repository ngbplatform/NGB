using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

/// <summary>
/// P0: Scale invariant for amount (NUMERIC(18,4)) must be enforced at the platform boundary.
/// Otherwise the DB will round and Trial Balance may drift (e.g. 0.0001).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AmountScaleInvariant_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_WhenAmountHasMoreThan4Decimals_MustThrow_AndWriteNothing()
    {
        using var host = CreateHost();

        await CreateMinimalPostingAccountsAsync(host);

        var documentId = Guid.CreateVersion7();
        var period = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            var act = async () =>
                await engine.PostAsync(async (ctx, ct) =>
                {
                    var coa = await ctx.GetChartOfAccountsAsync(ct);
                    var debit = coa.Get("50");
                    var credit = coa.Get("90.1");

                    // 5 decimals => must be rejected.
                    ctx.Post(documentId, period, debit, credit, 1.00001m);
                }, ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
            ex.Which.ParamName.Should().Be("entries");
            ex.Which.Message.Should().Match("*too many decimal places*scale=5*max=4*");
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // Defense: ensure no partial writes were persisted.
        (await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @id;",
                new { id = documentId }))
            .Should().Be(0);

        // Turnovers are aggregated by (period, account_id, dimension_set_id) and do not have document_id.
        // Since the post was rejected at validation boundary, no rows must be written at all.
        (await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM accounting_turnovers;"))
            .Should().Be(0);

        (await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id;",
                new { id = documentId }))
            .Should().Be(0);
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);

    private static async Task CreateMinimalPostingAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await coa.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        await coa.CreateAsync(new CreateAccountRequest(
                Code: "90.1",
                Name: "Retained Earnings",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);
    }
}
