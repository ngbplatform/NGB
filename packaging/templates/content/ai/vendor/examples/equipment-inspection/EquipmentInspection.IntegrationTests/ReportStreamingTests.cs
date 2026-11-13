using System.Globalization;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class ReportStreamingTests
{
    [Fact]
    public async Task Streaming_bounds_batches_preserves_its_snapshot_and_releases_the_session_after_cancellation()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Streaming inspection bench")));
        var expected = Enumerable.Range(0, 501)
            .Select(index => "Result " + index.ToString("D4", CultureInfo.InvariantCulture)).ToArray();
        foreach (var result in expected)
            await AddInspectionAsync(admin, equipment.Id, result);

        await using var scope = host.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<IStreamingReportDataSource>();
        var session = scope.ServiceProvider.GetRequiredService<IReportReadSession>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var query = Query();
        await session.BeginAsync(CancellationToken.None);
        var firstSnapshot = session.SnapshotId;
        Assert.NotEqual(Guid.Empty, firstSnapshot);
        Assert.True(unitOfWork.HasActiveTransaction);
        try
        {
            await using var stream = source.ReadAsync(query, CancellationToken.None).GetAsyncEnumerator();
            Assert.True(await stream.MoveNextAsync());
            Assert.Equal(500, stream.Current.Rows.Count);
            var rows = stream.Current.Rows.ToList();
            await AddInspectionAsync(admin, equipment.Id, "Result 0501");
            Assert.True(await stream.MoveNextAsync());
            Assert.Single(stream.Current.Rows);
            rows.AddRange(stream.Current.Rows);
            Assert.False(await stream.MoveNextAsync());
            Assert.Equal(expected, rows.Select(row => Assert.IsType<string>(row.Values["result"])));
            Assert.All(rows, row => Assert.Equal(1L, row.Values["count"]));
            Assert.Equal(firstSnapshot, session.SnapshotId);
        }
        finally
        {
            await session.EndAsync(CancellationToken.None);
        }
        Assert.False(unitOfWork.HasActiveTransaction);
        Assert.Equal(Guid.Empty, session.SnapshotId);

        await session.BeginAsync(CancellationToken.None);
        Assert.NotEqual(firstSnapshot, session.SnapshotId);
        using var cancellation = new CancellationTokenSource();
        try
        {
            await using var stream = source.ReadAsync(query, cancellation.Token).GetAsyncEnumerator();
            Assert.True(await stream.MoveNextAsync());
            Assert.Equal(500, stream.Current.Rows.Count);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.MoveNextAsync());
        }
        finally
        {
            await session.EndAsync(CancellationToken.None);
        }
        Assert.False(unitOfWork.HasActiveTransaction);
        Assert.Equal(Guid.Empty, session.SnapshotId);

        await session.BeginAsync(CancellationToken.None);
        try
        {
            var rows = new List<ReportDataRow>();
            await foreach (var page in source.ReadAsync(query, CancellationToken.None))
            {
                Assert.InRange(page.Rows.Count, 1, 500);
                rows.AddRange(page.Rows);
            }
            Assert.Equal(expected.Append("Result 0501"), rows.Select(row => Assert.IsType<string>(row.Values["result"])));
            Assert.All(rows, row => Assert.Equal(1L, row.Values["count"]));
            var emptyQuery = query with
            {
                Parameters = [new ReportPlanParameter("from_utc", "2000-01-01"), new ReportPlanParameter("to_utc", "2000-01-02")]
            };
            await foreach (var page in source.ReadAsync(emptyQuery, CancellationToken.None))
                Assert.Empty(page.Rows);
        }
        finally
        {
            await session.EndAsync(CancellationToken.None);
        }
        Assert.False(unitOfWork.HasActiveTransaction);
        Assert.Equal(Guid.Empty, session.SnapshotId);
    }

    private static ReportDataQuery Query()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new ReportDataQuery(
            ReportCode: InspectionReports.Summary,
            DatasetCode: InspectionReports.Summary,
            RowGroups: [new ReportPlanGrouping("result", "result", "Result", "string")],
            ColumnGroups: [],
            DetailFields: [],
            Measures: [new ReportPlanMeasure("inspection_count", "count", "Inspections", "int64", ReportAggregationKind.Count)],
            Sorts: [new ReportPlanSort("result", null, ReportSortDirection.Asc)],
            Predicates: [],
            Parameters:
            [
                new ReportPlanParameter("from_utc", today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new ReportPlanParameter("to_utc", today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            ]);
    }

    private static async Task AddInspectionAsync(HttpClient admin, Guid equipmentId, string result)
    {
        var document = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
            CompleteInspectionPayload(equipmentId, result));
        var path = "/api/documents/example.inspection/" + document.Id;
        var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        using var posted = await ExecutePostAsync(admin, path, state!.DocumentVersion, document.Id.ToString());
        await EnsureSuccessAsync(posted);
    }
}
