using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Reporting;
using NGB.PostgreSql.Reporting.Accounting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.Testing.Reporting;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_SqlOptimizationCompatibilityTests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Protected_http_cursors_and_pm_filters_survive_switching_sql_sources(bool optimizedFirst)
    {
        await using var factory = new SwitchingFactory(fixture);
        using var client = factory.CreateClient();
        await SeedAsync(factory);
        var request = FlatRequest();
        var expected = await Execute(request with { Limit = 100 }, optimized: false);
        var actual = await Execute(request with { Limit = 100 }, optimized: true);
        JsonSerializer.Serialize(actual.Sheet, Json).Should().Be(JsonSerializer.Serialize(expected.Sheet, Json));
        expected.Sheet.Rows.Should().NotBeEmpty();

        var collected = new List<ReportSheetRowDto>();
        string? cursor = null;
        var pageNumber = 0;
        do
        {
            var pageRequest = request with { Cursor = cursor, Limit = pageNumber == 0 ? 1 : 3 };
            var oldPage = await Execute(pageRequest, optimized: false);
            var newPage = await Execute(pageRequest, optimized: true);
            JsonSerializer.Serialize(newPage.Sheet, Json).Should().Be(JsonSerializer.Serialize(oldPage.Sheet, Json));
            newPage.HasMore.Should().Be(oldPage.HasMore);
            newPage.Offset.Should().Be(oldPage.Offset);
            var page = (pageNumber % 2 == 0) == optimizedFirst ? newPage : oldPage;
            collected.AddRange(page.Sheet.Rows);
            if (!page.HasMore)
                break;

            page.NextCursor.Should().NotBeNullOrWhiteSpace().And.NotBe(cursor);
            cursor = page.NextCursor;
            pageNumber++;
            pageNumber.Should().BeLessThan(20);
        } while (true);
        JsonSerializer.Serialize(collected, Json).Should().Be(JsonSerializer.Serialize(expected.Sheet.Rows, Json));

        foreach (var field in new[] { "property_id", "party_id", "lease_id" })
        {
            var scoped = request with
            {
                Filters = new Dictionary<string, ReportFilterValueDto>
                {
                    [field] = new(JsonSerializer.SerializeToElement(Guid.NewGuid()), IncludeDescendants: false)
                }
            };
            var filteredOld = await Execute(scoped, optimized: false);
            var filteredNew = await Execute(scoped, optimized: true);
            JsonSerializer.Serialize(filteredNew.Sheet, Json).Should().Be(JsonSerializer.Serialize(filteredOld.Sheet, Json));
            filteredNew.Sheet.Rows.Should().BeEmpty("PM filters must never be dropped by the unfiltered fast path");
        }

        async Task<ReportExecutionResponseDto> Execute(ReportExecutionRequestDto execution, bool optimized)
        {
            factory.UseOptimized = optimized;
            using var response = await client.PostAsJsonAsync("/api/reports/accounting.ledger.analysis/execute", execution);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json))!;
        }
    }

    [Fact]
    public async Task Complete_pm_pivot_returns_the_same_cells_and_totals_with_three_register_queries()
    {
        await using var factory = new SwitchingFactory(fixture);
        using var client = factory.CreateClient();
        await SeedAsync(factory);
        var request = FlatRequest() with
        {
            Limit = 100,
            Layout = new ReportLayoutDto(
                RowGroups: [new("account_display")],
                ColumnGroups: [new("period_utc", ReportTimeGrain.Month)],
                Measures: [new("debit_amount", ReportAggregationKind.Sum), new("credit_amount", ReportAggregationKind.Sum)],
                Sorts: [new("account_display"), new("period_utc", ReportSortDirection.Asc, ReportTimeGrain.Month, AppliesToColumnAxis: true)],
                ShowDetails: false, ShowSubtotals: true, ShowGrandTotals: true)
        };
        factory.UseOptimized = false;
        using var original = await client.PostAsJsonAsync("/api/reports/accounting.ledger.analysis/execute", request);
        original.StatusCode.Should().Be(HttpStatusCode.OK);
        var expected = (await original.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json))!;
        using var firstPageResponse = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute", request with { Limit = 1 });
        firstPageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstPage = (await firstPageResponse.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json))!;
        firstPage.HasMore.Should().BeTrue();
        using var lastPageResponse = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/execute", request with { Cursor = firstPage.NextCursor });
        lastPageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var lastPage = (await lastPageResponse.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json))!;
        lastPage.HasMore.Should().BeFalse();

        factory.UseOptimized = true;
        await using var scope = factory.Services.CreateAsyncScope();
        using var probe = new ReportPerformanceProbe("accounting.ledger.analysis", "pm-optimized-pivot");
        var actual = await scope.ServiceProvider.GetRequiredService<IReportEngine>()
            .ExecuteAsync("accounting.ledger.analysis", request, default);

        JsonSerializer.Serialize(actual.Sheet, Json).Should().Be(JsonSerializer.Serialize(expected.Sheet, Json));
        actual.HasMore.Should().BeFalse();
        probe.SqlCommands.Count(sql => sql.Contains("FROM accounting_register_main", StringComparison.Ordinal))
            .Should().Be(3);
        var total = actual.Sheet.Rows.Should().ContainSingle(row => row.RowKind == ReportRowKind.Total).Which;
        JsonSerializer.Serialize(total, Json).Should().Be(
            JsonSerializer.Serialize(lastPage.Sheet.Rows.Single(row => row.RowKind == ReportRowKind.Total), Json));
    }

    private static ReportExecutionRequestDto FlatRequest() => new(
        Parameters: new Dictionary<string, string> { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-10-31" },
        Layout: new ReportLayoutDto(
            DetailFields: ["period_utc", "account_display", "document_display"],
            Measures:
            [
                new("debit_amount", ReportAggregationKind.Sum),
                new("credit_amount", ReportAggregationKind.Sum),
                new("net_amount", ReportAggregationKind.Sum)
            ],
            Sorts: [new("period_utc"), new("account_display")],
            ShowDetails: false, ShowSubtotals: false, ShowGrandTotals: false));

    private static async Task SeedAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        await uow.Connection.ExecuteAsync("""
            INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
            VALUES (md5('compat-cash')::uuid, 'IT-CASH', 'Cash', 0, 1, 0),
                   (md5('compat-revenue')::uuid, 'IT-REVENUE', 'Revenue', 3, 4, 0);
            INSERT INTO documents(id, type_code, number, date_utc, status)
            SELECT md5('compat-' || g)::uuid, 'pm.rent_charge', 'COMPAT-' || g, '2026-09-01', 1
            FROM generate_series(1, 8) g;
            INSERT INTO accounting_register_main(document_id, period, debit_account_id, credit_account_id, amount)
            SELECT md5('compat-' || g)::uuid,
                   CASE WHEN g <= 4 THEN '2026-09-01'::timestamptz ELSE '2026-10-01'::timestamptz END,
                   md5('compat-cash')::uuid, md5('compat-revenue')::uuid, g * 1.25
            FROM generate_series(1, 8) g;
            """);
    }

    private sealed class SwitchingFactory(PmIntegrationFixture fixture) : PmApiFactory(fixture)
    {
        public bool UseOptimized { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(provider =>
                new PostgresReportDatasetCatalog(provider.GetServices<IPostgresReportDatasetSource>()
                    .Select(source => new SwitchingSource(source, this))))));
        }
    }

    private sealed class SwitchingSource(IPostgresReportDatasetSource original, SwitchingFactory factory)
        : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
        {
            return original.GetDatasets().Select(source => source.DatasetCodeNorm != "accounting.ledger.analysis"
                ? source
                : new PostgresReportDatasetBinding(
                    source.DatasetCodeNorm,
                    source.FromSql,
                    source.Fields.Values.ToArray(),
                    source.Measures.Values.ToArray(),
                    source.BaseWhereSql,
                    source.CursorKeyFields.Select(field => field.FieldCodeNorm).ToArray(),
                    request => factory.UseOptimized
                        ? AccountingLedgerAnalysisPostgresDatasetSource.SelectAccountAggregateSource(request)
                        : request.DetailFields.Count == 0 ? LegacyAccountPeriodSource : null))
                .ToArray();
        }
    }

    // Frozen pre-optimization account/period SQL. The outer builder supplies the
    // account-only shape too; retain numeric scale so the HTTP display is compared exactly.
    private static readonly PostgresReportSqlSource LegacyAccountPeriodSource = new("""
        (
            SELECT a.account_id, a.code AS account_code, a.name AS account_name,
                   a.code || ' — ' || a.name AS account_display, r.period,
                   SUM(r.debit_amount) AS debit_amount, SUM(r.credit_amount) AS credit_amount,
                   SUM(r.debit_amount - r.credit_amount) AS net_amount
            FROM (
                SELECT debit_account_id AS account_id, period, SUM(amount) AS debit_amount, 0::numeric AS credit_amount
                FROM accounting_register_main
                WHERE period >= @from_utc AND period < @to_utc_exclusive
                GROUP BY debit_account_id, period
                UNION ALL
                SELECT credit_account_id AS account_id, period, 0::numeric AS debit_amount, SUM(amount) AS credit_amount
                FROM accounting_register_main
                WHERE period >= @from_utc AND period < @to_utc_exclusive
                GROUP BY credit_account_id, period
            ) r
            JOIN accounting_accounts a ON a.account_id = r.account_id AND a.is_deleted = FALSE
            GROUP BY a.account_id, a.code, a.name, r.period
        ) x
        """);
}
