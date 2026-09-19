using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Contracts.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class TrialBalanceStreamingExportTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Code = "accounting.trial_balance";
    private static ReportExecutionRequestDto Request => new(Parameters: new Dictionary<string, string>
        { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-09-12" });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Aggregates_over_ten_thousand_dimension_sets_in_all_snapshot_modes_and_filters_before_summing(int snapshotMode)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cash, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        var dimension = Guid.NewGuid();
        var selected = Guid.NewGuid();
        var other = Guid.NewGuid();
        var secondDimension = Guid.NewGuid();
        var secondSelected = Guid.NewGuid();
        await ExecuteAsync(host, """
            INSERT INTO platform_dimensions(dimension_id,code,name) VALUES (@dimension,'it_report_scope','Scope'),(@secondDimension,'it_report_second_scope','Second scope');
            INSERT INTO platform_dimension_sets(dimension_set_id) SELECT md5('tb-set-'||g)::uuid FROM generate_series(1,10001) g;
            INSERT INTO platform_dimension_set_items(dimension_set_id,dimension_id,value_id)
                SELECT md5('tb-set-'||g)::uuid,@dimension,CASE WHEN g%2=0 THEN @selected ELSE @other END FROM generate_series(1,10001) g;
            INSERT INTO platform_dimension_set_items(dimension_set_id,dimension_id,value_id)
                SELECT md5('tb-set-'||g)::uuid,@secondDimension,CASE WHEN g%3=0 THEN @secondSelected ELSE @other END FROM generate_series(1,10001) g;
            INSERT INTO accounting_turnovers(period,account_id,dimension_set_id,debit_amount,credit_amount)
                SELECT '2026-08-01'::date,@cash,md5('tb-set-'||g)::uuid,10,0 FROM generate_series(1,10001) g
                UNION ALL SELECT '2026-09-01'::date,@cash,md5('tb-set-'||g)::uuid,2,0 FROM generate_series(1,10001) g;
            """, new { cash, dimension, selected, other, secondDimension, secondSelected });
        if (snapshotMode > 0)
        {
            var period = snapshotMode == 1 ? new DateOnly(2026, 8, 1) : new DateOnly(2026, 9, 1);
            await ExecuteAsync(host, """
                INSERT INTO accounting_balances(period,account_id,dimension_set_id,opening_balance,closing_balance)
                    SELECT @period,@cash,md5('tb-set-'||g)::uuid,@opening,@closing FROM generate_series(1,10001) g;
                INSERT INTO accounting_closed_periods(period,closed_at_utc,closed_by) VALUES (@period,now(),'test');
                """, new { period, cash, opening = snapshotMode == 1 ? 0m : 10m, closing = snapshotMode == 1 ? 10m : 12m });
        }
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ITrialBalanceAccountSummaryReader>();
        var all = await CollectAsync(reader.ReadAsync(new(2026, 9, 1), new(2026, 9, 1), null));
        all.Should().ContainSingle();
        all[0].OpeningBalance.Should().Be(100010m);
        all[0].DebitAmount.Should().Be(20002m);
        var filtered = await CollectAsync(reader.ReadAsync(new(2026, 9, 1), new(2026, 9, 1), new([new(dimension, [selected])])));
        filtered.Single().OpeningBalance.Should().Be(50000m);
        filtered.Single().DebitAmount.Should().Be(10000m);
        var both = await CollectAsync(reader.ReadAsync(new(2026, 9, 1), new(2026, 9, 1), new([new(dimension, [selected, other])])));
        both.Should().BeEquivalentTo(all);
        var intersection = await CollectAsync(reader.ReadAsync(new(2026, 9, 1), new(2026, 9, 1),
            new([new(dimension, [selected]), new(secondDimension, [secondSelected])])));
        intersection.Single().OpeningBalance.Should().Be(16660m);
        intersection.Single().DebitAmount.Should().Be(3332m);
        var missing = await CollectAsync(reader.ReadAsync(new(2026, 9, 1), new(2026, 9, 1), new([new(dimension, [Guid.NewGuid()])])));
        missing.Should().BeEmpty();
    }

    [Fact]
    public async Task Large_xlsx_contains_every_account_and_correct_totals()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ExecuteAsync(host, """
            INSERT INTO accounting_accounts(account_id,code,name,account_type,statement_section,negative_balance_policy)
                SELECT md5('tb-account-'||g)::uuid,'IT'||lpad(g::text,5,'0'),'Account '||g,0,1,0 FROM generate_series(1,10001) g;
            INSERT INTO accounting_turnovers(period,account_id,dimension_set_id,debit_amount,credit_amount)
                SELECT '2026-09-01'::date,md5('tb-account-'||g)::uuid,'00000000-0000-0000-0000-000000000000'::uuid,2,0 FROM generate_series(1,10001) g;
            """, null);
        await using var scope = host.Services.CreateAsyncScope();
        var sheet = await DirectAccountingExportsTests.RunAsync(host, Code, Request);
        sheet.Rows.Count(r => r.RowKind == ReportRowKind.Detail).Should().Be(10001);
        sheet.Rows.Last().Cells[1].Value!.Value.GetDecimal().Should().Be(20002m);
    }

    private static async Task ExecuteAsync(IHost host, string sql, object? args)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await uow.Connection.ExecuteAsync(sql, args, uow.Transaction);
        await uow.CommitAsync();
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var row in source) result.Add(row);
        return result;
    }
}
