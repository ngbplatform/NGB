using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Testing.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class LedgerAnalysisStreamingExportTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Code = "accounting.ledger.analysis";
    private const int UniqueDocuments = 1001;

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public async Task Document_labels_that_depend_on_the_grain_preserve_groups_and_original_observation_totals(
        bool pivot, bool documentsAsDetails, bool grandTotals)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedAsync(host, UniqueDocuments);
        using var probe = new ReportPerformanceProbe(Code, "document-label-grain");
        var sheet = await DirectAccountingExportsTests.RunAsync(host, Code, Request(new(
            RowGroups: documentsAsDetails ? [new("account_display")] : [new("account_display"), new("document_display")],
            ColumnGroups: pivot ? [new("period_utc", ReportTimeGrain.Week)] : [],
            DetailFields: documentsAsDetails ? ["document_display"] : pivot ? [] : ["period_utc"],
            Measures: Measures,
            Sorts: pivot
                ? [new("account_display"), new("period_utc", ReportSortDirection.Desc, ReportTimeGrain.Week, AppliesToColumnAxis: true)]
                : [new("account_display")],
            ShowDetails: documentsAsDetails || !pivot,
            ShowSubtotals: true, ShowSubtotalsOnSeparateRows: false, ShowGrandTotals: grandTotals)));

        var labelIndex = documentsAsDetails ? 1 : 0;
        var shared = sheet.Rows.Where(r => r.Cells[labelIndex].Display == "Shared").ToArray();
        shared.Should().HaveCount(2, "the same number in two document types is one selected display-value group per account");
        shared.Should().OnlyContain(r => r.Cells[labelIndex].Action == null,
            "an aggregate containing different documents must not link to either arbitrary document");
        sheet.Rows.Count(r => r.RowKind == ReportRowKind.Group && r.OutlineLevel == 0).Should().Be(2);
        if (documentsAsDetails)
            sheet.Rows.Count(r => r.RowKind == ReportRowKind.Detail).Should().Be(2 * (UniqueDocuments + 1));
        else
            sheet.Rows.Count(r => r.RowKind == ReportRowKind.Group && r.OutlineLevel == 1).Should().Be(2 * (UniqueDocuments + 1));

        if (!pivot || grandTotals)
        {
            ReadMeasures(shared[0]).Should().Equal(18m, 0m, 18m, 3m, 10m, 6m);
            ReadMeasures(shared[1]).Should().Equal(0m, 18m, -18m, 0m, 0m, 0m);
            var cash = sheet.Rows.Single(r => r.RowKind == ReportRowKind.Group && r.Cells[0].Display == "50 — Cash");
            ReadMeasures(cash)[0].Should().Be(UniqueDocuments * 2m + 18m);
            ReadMeasures(cash)[5].Should().BeApproximately((UniqueDocuments * 2m + 18m) / (UniqueDocuments + 3), 0.00000001m);
        }

        if (pivot)
        {
            // Weeks are descending: Sep 14 contains 10; Sep 7 contains two observations, 3 and 5.
            ReadMeasures(shared[0], labelIndex + 1).Should().Equal(10m, 0m, 10m, 10m, 10m, 10m);
            ReadMeasures(shared[0], labelIndex + 1 + Measures.Length).Should().Equal(8m, 0m, 8m, 3m, 5m, 4m);
        }

        sheet.Rows.Count(r => r.RowKind == ReportRowKind.Total).Should().Be(grandTotals ? 1 : 0);
        if (grandTotals)
        {
            var total = ReadMeasures(sheet.Rows.Last());
            total.Take(5).Should().Equal(UniqueDocuments * 2m + 18m, UniqueDocuments * 2m + 18m, 0m, 0m, 10m);
            total[5].Should().BeApproximately((UniqueDocuments * 2m + 18m) / (2 * (UniqueDocuments + 3)), 0.00000001m);
        }

        probe.SqlCommands.Count(sql => sql.StartsWith("DECLARE ", StringComparison.Ordinal)).Should().BeLessThan(10,
            "source queries depend on hierarchy depth, not the number of documents");
        probe.SqlCommands.Count(sql => sql.Contains("WHERE id = ANY(@Ids)", StringComparison.Ordinal)).Should().BeInRange(1, 30,
            "document displays must be resolved in batches across cursor fetch boundaries");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Different_source_groups_with_identical_enriched_labels_remain_separate(bool pivot)
    {
        var displays = new SameLabelReader();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString,
            services => services.AddSingleton<IDocumentDisplayReader>(displays));
        await SeedAsync(host, UniqueDocuments);
        var sheet = await DirectAccountingExportsTests.RunAsync(host, Code, Request(new(
            RowGroups: [new("account_display"), new("document_display")],
            ColumnGroups: pivot ? [new("period_utc", ReportTimeGrain.Week)] : [],
            Measures: Measures, ShowSubtotals: true, ShowGrandTotals: true)));

        var documents = sheet.Rows.Where(r => r.RowKind == ReportRowKind.Group && r.OutlineLevel == 1).ToArray();
        documents.Should().HaveCount(2 * (UniqueDocuments + 1));
        var sameLabel = documents.Where(r => r.Cells[0].Display == "Same label").ToArray();
        sameLabel.Should().HaveCount(2 * UniqueDocuments);
        sameLabel.Select(r => r.GroupKey).Should().OnlyHaveUniqueItems();
        sameLabel.Should().OnlyContain(r => r.Cells[0].Action != null);
        displays.BatchSizes.Should().OnlyContain(size => size > 0 && size <= 500);
        displays.BatchSizes.Count.Should().BeLessThan(30);
    }

    [Fact]
    public async Task Document_pivot_columns_use_source_keys_even_when_each_week_has_a_different_display_label()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await SeedAsync(host, 0);
        var sheet = await DirectAccountingExportsTests.RunAsync(host, Code, Request(new(
            RowGroups: [new("period_utc", ReportTimeGrain.Week)],
            ColumnGroups: [new("document_display")], Measures: Measures,
            ShowSubtotals: true, ShowGrandTotals: true)));

        sheet.HeaderRows!.First().Cells.Should().Contain(c => c.Display == "Shared");
        var groups = sheet.Rows.Where(r => r.RowKind == ReportRowKind.Group).ToArray();
        groups.Should().HaveCount(2);
        ReadMeasures(groups[0], 1).Should().Equal(8m, 8m, 0m, 0m, 5m, 2m);
        ReadMeasures(groups[1], 1).Should().Equal(10m, 10m, 0m, 0m, 10m, 5m);
        ReadMeasures(sheet.Rows.Last(), 1).Should().Equal(18m, 18m, 0m, 0m, 10m, 3m);
    }

    private static readonly ReportMeasureSelectionDto[] Measures =
    [
        new("debit_amount", ReportAggregationKind.Sum),
        new("credit_amount", ReportAggregationKind.Sum),
        new("net_amount", ReportAggregationKind.Sum),
        new("debit_amount", ReportAggregationKind.Min),
        new("debit_amount", ReportAggregationKind.Max),
        new("debit_amount", ReportAggregationKind.Average)
    ];

    private static decimal[] ReadMeasures(ReportSheetRowDto row, int? start = null)
        => row.Cells.Skip(start ?? row.Cells.Count - Measures.Length).Take(Measures.Length)
            .Select(c => c.Value!.Value.GetDecimal()).ToArray();

    private static ReportExecutionRequestDto Request(ReportLayoutDto layout)
        => new(Layout: layout, Parameters: new Dictionary<string, string>
            { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-09-20" });

    private static async Task SeedAsync(IHost host, int count)
    {
        var (cash, revenue, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await DirectAccountingExportsTests.ExecuteAsync(host, """
            INSERT INTO documents(id,type_code,number,date_utc,status)
              SELECT md5('export-'||g)::uuid,'it_doc_a','D'||lpad(g::text,6,'0'),'2026-09-07'::timestamptz,1
              FROM generate_series(1,@count) g;
            INSERT INTO documents(id,type_code,number,date_utc,status) VALUES
              (md5('shared-a')::uuid,'it_doc_a','Shared','2026-09-07'::timestamptz,1),
              (md5('shared-b')::uuid,'it_doc_b','Shared','2026-09-14'::timestamptz,1);
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount)
              SELECT md5('export-'||g)::uuid,'2026-09-07'::timestamptz,@cash,@revenue,2
              FROM generate_series(1,@count) g;
            INSERT INTO accounting_register_main(document_id,period,debit_account_id,credit_account_id,amount) VALUES
              (md5('shared-a')::uuid,'2026-09-07'::timestamptz,@cash,@revenue,3),
              (md5('shared-a')::uuid,'2026-09-08'::timestamptz,@cash,@revenue,5),
              (md5('shared-b')::uuid,'2026-09-14'::timestamptz,@cash,@revenue,10);
            """, new { cash, revenue, count });
    }

    private sealed class SameLabelReader : IDocumentDisplayReader
    {
        public List<int> BatchSizes { get; } = [];
        public async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => (await ResolveRefsAsync(ids, ct)).ToDictionary(p => p.Key, p => p.Value.Display);

        public Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        {
            BatchSizes.Add(ids.Count);
            return Task.FromResult<IReadOnlyDictionary<Guid, DocumentDisplayRef>>(
                ids.ToDictionary(id => id, id => new DocumentDisplayRef(id, "it_doc_a", "Same label")));
        }
    }
}
