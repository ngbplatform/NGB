using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Testing.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class AccountCardBrowsingTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Dense_cancelled_ranges_have_a_fixed_query_budget_and_still_reach_the_first_effective_line()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cash, revenue, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        await uow.Connection.ExecuteAsync("""
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount)
            SELECT md5('cancelled-'||g)::uuid,'2026-01-01'::timestamptz,@cash,@revenue,1 FROM generate_series(1,10001) g;
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount)
            SELECT md5('cancelled-'||g)::uuid,'2026-01-02'::timestamptz,@revenue,@cash,1 FROM generate_series(1,10001) g;
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount)
            VALUES (gen_random_uuid(),'2026-01-03',@cash,@revenue,9);
            """, new { cash, revenue });
        var report = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        using var probe = new ReportPerformanceProbe("accounting.account_card", "cancelled-20002");
        var page = await report.ExecuteAsync("accounting.account_card", Request(cash), default);
        page.HasMore.Should().BeFalse();
        page.Sheet.Rows.Should().ContainSingle();
        page.Sheet.Rows[0].Cells[^1].Value!.Value.GetDecimal().Should().Be(9);
        probe.CommandCount.Should().BeInRange(1, 20);
        probe.SqlCommands.Count(sql => sql.Contains("candidates AS MATERIALIZED")).Should().Be(4);
    }

    [Fact]
    public async Task Opposite_lines_with_different_dimensions_survive_one_row_paging_at_the_same_timestamp()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cash, revenue, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        var document = Guid.NewGuid();
        var first = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("20000000-0000-0000-0000-000000000001");
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        await uow.Connection.ExecuteAsync("""
            INSERT INTO platform_dimension_sets(dimension_set_id) VALUES (@first),(@second);
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,debit_dimension_set_id,credit_dimension_set_id,amount)
            VALUES (@document,'2026-01-03',@cash,@revenue,@first,@second,9),
                   (@document,'2026-01-03',@revenue,@cash,@first,@second,9);
            """, new { cash, revenue, first, second, document });
        var report = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        var one = await report.ExecuteAsync("accounting.account_card", Request(cash), default);
        one.HasMore.Should().BeTrue();
        one.Sheet.Rows.Should().ContainSingle().Which.Cells[^1].Value!.Value.GetDecimal().Should().Be(9);
        var two = await report.ExecuteAsync("accounting.account_card", Request(cash) with { Cursor = one.NextCursor }, default);
        two.HasMore.Should().BeFalse();
        two.Sheet.Rows.Should().ContainSingle().Which.Cells[^1].Value!.Value.GetDecimal().Should().Be(0);
    }

    private static ReportExecutionRequestDto Request(Guid cash) => new(Limit: 1,
        Parameters: new Dictionary<string, string> { ["from_utc"] = "2026-01-01", ["to_utc"] = "2026-01-31" },
        Filters: new Dictionary<string, ReportFilterValueDto> { ["account_id"] = new(JsonSerializer.SerializeToElement(cash)) });
}
