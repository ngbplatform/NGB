using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Reporting.Runs;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class TrialBalanceSavedRuns_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
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
    public async Task Large_result_pages_and_xlsx_are_complete_immutable_and_owner_scoped()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await ExecuteAsync(host, """
            INSERT INTO accounting_accounts(account_id,code,name,account_type,statement_section,negative_balance_policy)
                SELECT md5('tb-account-'||g)::uuid,'IT'||lpad(g::text,5,'0'),'Account '||g,0,1,0 FROM generate_series(1,10001) g;
            INSERT INTO accounting_turnovers(period,account_id,dimension_set_id,debit_amount,credit_amount)
                SELECT '2026-09-01'::date,md5('tb-account-'||g)::uuid,'00000000-0000-0000-0000-000000000000'::uuid,2,0 FROM generate_series(1,10001) g;
            """, null);
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportRunService>();
        var run = await service.StartAsync(Code, "alice", Request, default);
        await host.Services.GetRequiredService<ReportRunProcessor>().ProcessNextAsync(default);
        (await service.GetAsync(Code, "alice", run.Id, default))!.Status.Should().Be("Ready");
        (await service.GetAsync(Code, "bob", run.Id, default)).Should().BeNull();
        await ((Func<Task>)(() => service.ReadAsync(Code, "bob", run.Id, 0, 100, default))).Should().ThrowAsync<ReportRunNotFoundException>();
        await ((Func<Task>)(() => service.ExportAsync(Code, "bob", run.Id, default))).Should().ThrowAsync<ReportRunNotFoundException>();
        await ((Func<Task>)(() => service.ReadAsync("accounting.other", "alice", run.Id, 0, 100, default))).Should().ThrowAsync<ReportRunNotFoundException>();

        var initial = await service.ReadAsync(Code, "alice", run.Id, 0, 257, default);
        var continued = await service.ContinueAsync(Code, "alice", Request with { Cursor = initial.NextCursor, Limit = 257 }, default);
        continued.Offset.Should().Be(257);
        await ((Func<Task>)(() => service.ContinueAsync(Code, "alice", Request with
        {
            Cursor = initial.NextCursor,
            Parameters = new Dictionary<string, string> { ["from_utc"] = "2026-08-01", ["to_utc"] = "2026-09-12" }
        }, default))).Should().ThrowAsync<NgbArgumentInvalidException>();

        // A later posting/rename cannot change an already published result or its export.
        await ExecuteAsync(host, "UPDATE accounting_turnovers SET debit_amount=999; UPDATE accounting_accounts SET name='Renamed';", null);
        var seen = new HashSet<string>();
        var rows = new List<ReportSheetRowDto>();
        var offset = 0;
        while (true)
        {
            var page = await service.ReadAsync(Code, "alice", run.Id, offset, 257, default);
            page.Total.Should().Be(10004);
            page.Sheet.Rows.Should().HaveCountLessThanOrEqualTo(257);
            rows.AddRange(page.Sheet.Rows);
            foreach (var detail in page.Sheet.Rows.Where(r => r.RowKind == ReportRowKind.Detail))
                seen.Add(detail.GroupKey!).Should().BeTrue("pages must not duplicate rows");
            if (!page.HasMore) break;
            offset += page.Sheet.Rows.Count;
        }
        seen.Should().HaveCount(10001);
        rows.Last().RowKind.Should().Be(ReportRowKind.Total);
        rows.Last().Cells[1].Value!.Value.GetDecimal().Should().Be(20002m);
        rows.SelectMany(r => r.Cells).Should().NotContain(c => c.Display == "Renamed");
        JsonSerializer.Serialize((await service.ReadAsync(Code, "alice", run.Id, 0, 257, default)).Sheet.Rows)
            .Should().Be(JsonSerializer.Serialize(rows.Take(257)));
        await using var exported = await service.ExportAsync(Code, "alice", run.Id, default);
        await using var zip = new ZipArchive(exported, ZipArchiveMode.Read, leaveOpen: true);
        await using var xml = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var document = XDocument.Load(xml);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetRows = document.Descendants(ns + "row").ToArray();
        sheetRows.Should().HaveCount(10005); // headers + every persisted result row
        sheetRows.Last().Elements(ns + "c").ElementAt(1).Element(ns + "v")!.Value.Should().Be("20002");
    }

    [Fact]
    public async Task Queue_claims_are_exclusive_and_expired_attempts_cannot_publish()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var a = host.Services.CreateAsyncScope();
        await using var b = host.Services.CreateAsyncScope();
        var first = a.ServiceProvider.GetRequiredService<IReportRunStore>();
        var second = b.ServiceProvider.GetRequiredService<IReportRunStore>();
        var id = Guid.NewGuid();
        await first.CreateAsync(id, "alice", Code, "{}", default);
        var claims = await Task.WhenAll(first.ClaimAsync([Code], default), second.ClaimAsync([Code], default));
        claims.Count(x => x is not null).Should().Be(1);
        var original = claims.Single(x => x is not null)!;
        await first.AppendAsync(id, original.Attempt, 0, ["{}"], default);
        await ExecuteAsync(host, "UPDATE platform_report_runs SET lease_until_utc=now()-interval '1 minute' WHERE id=@id", new { id });
        var recovered = (await second.ClaimAsync([Code], default))!;
        recovered.Attempt.Should().NotBe(original.Attempt);
        (await first.RenewAsync(id, original.Attempt, default)).Should().BeFalse();
        (await first.CompleteAsync(id, original.Attempt, "{}", 1, default)).Should().BeFalse();
        await second.AppendAsync(id, recovered.Attempt, 0, ["{}", "{}"], default);
        await second.WriteAsync(id, recovered.Attempt, [0], ["{\"final\":true}"], default);
        (await second.CompleteAsync(id, recovered.Attempt, "{}", 3, default)).Should().BeFalse("partial results must never be published");
        (await second.CompleteAsync(id, recovered.Attempt, "{}", 2, default)).Should().BeTrue();
        await second.WriteAsync(id, recovered.Attempt, [0], ["{}"], default);
        (await second.ReadRowsAsync(id, recovered.Attempt, 0, 1, default)).Single().Should().Contain("true", "published rows are immutable");
        await second.CleanupAsync(default);
        (await first.ReadRowsAsync(id, original.Attempt, 0, 10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_failure_expiration_and_shutdown_recovery_have_explicit_states()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportRunService>();
        var store = scope.ServiceProvider.GetRequiredService<IReportRunStore>();
        var queued = await service.StartAsync(Code, "alice", Request, default);
        await service.CancelAsync(Code, "bob", queued.Id, default);
        (await service.GetAsync(Code, "alice", queued.Id, default))!.Status.Should().Be("Queued");
        await service.CancelAsync(Code, "alice", queued.Id, default);
        (await host.Services.GetRequiredService<ReportRunProcessor>().ProcessNextAsync(default)).Should().BeFalse();
        await ((Func<Task>)(() => service.ReadAsync(Code, "alice", queued.Id, 0, 50, default))).Should().ThrowAsync<ReportRunNotReadyException>();
        var pending = await service.StartAsync(Code, "alice", Request, default);
        var claimed = (await store.ClaimAsync([Code], default))!;
        await service.CancelAsync(Code, "alice", pending.Id, default);
        (await store.RenewAsync(pending.Id, claimed.Attempt, default)).Should().BeFalse();
        (await store.CompleteAsync(pending.Id, claimed.Attempt, "{}", 0, default)).Should().BeFalse();
        var badId = Guid.NewGuid();
        await store.CreateAsync(badId, "alice", Code, "{}", default);
        await host.Services.GetRequiredService<ReportRunProcessor>().ProcessNextAsync(default);
        (await service.GetAsync(Code, "alice", badId, default))!.Status.Should().Be("Failed");
        var expired = await service.StartAsync(Code, "alice", Request, default);
        await ExecuteAsync(host, "UPDATE platform_report_runs SET expires_at_utc=now()-interval '1 minute' WHERE id=@id", new { id = expired.Id });
        (await service.GetAsync(Code, "alice", expired.Id, default)).Should().BeNull();
        await store.CleanupAsync(default);
        var orphaned = await service.StartAsync(Code, "alice", Request, default);
        await ExecuteAsync(host, "UPDATE platform_report_runs SET status='Running',attempts=3,lease_until_utc=now()-interval '1 minute' WHERE id=@id", new { id = orphaned.Id });
        await store.CleanupAsync(default);
        (await service.GetAsync(Code, "alice", orphaned.Id, default))!.Status.Should().Be("Failed");
    }

    [Fact]
    public async Task Expired_large_results_are_deleted_in_bounded_batches_and_cannot_be_published()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IReportRunStore>();
        var id = Guid.NewGuid();
        await store.CreateAsync(id, "owner", Code, "{}", default);
        var run = (await store.ClaimAsync([Code], default))!;
        await store.AppendAsync(id, run.Attempt, 0, Enumerable.Repeat("{}", 1001).ToArray(), default);
        await ExecuteAsync(host, "UPDATE platform_report_runs SET expires_at_utc=now()-interval '1 minute' WHERE id=@id", new { id });
        await store.AppendAsync(id, run.Attempt, 1001, ["{}"], default);
        (await store.CompleteAsync(id, run.Attempt, "{}", 1001, default)).Should().BeFalse();
        await store.CleanupAsync(default);
        (await store.ReadRowsAsync(id, run.Attempt, 0, 2000, default)).Should().HaveCount(1);
        await store.CleanupAsync(default);
        (await store.ReadRowsAsync(id, run.Attempt, 0, 2000, default)).Should().BeEmpty();
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
