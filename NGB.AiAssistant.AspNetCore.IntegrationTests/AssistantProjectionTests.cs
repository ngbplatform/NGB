using System.Text.Json;
using System.Text.Json.Serialization;
using NGB.Contracts.Audit;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Mcp;
using NGB.Mcp.Tools;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed class AssistantProjectionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RecordMetadataPreservesDeclaredFieldsFiltersAndParts(bool catalog, bool populated)
    {
        var list = populated ? new ListMetadataDto(
            [new("name", "Name", DataType.String)], [new("parent", "Parent", DataType.Guid)]) : null;
        var form = populated ? new FormMetadataDto([new("Main", [new([
            new("name", "Name", DataType.String, UiControl.Input)
        ])])]) : null;
        PartMetadataDto[]? parts = populated ? [new("lines", "Lines", list!)] : null;
        var metadata = catalog
            ? new MetadataResult(Catalog: new("sample.record", "Record", EntityKind.Catalog, List: list, Form: form, Parts: parts))
            : new MetadataResult(Document: new("sample.record", "Record", EntityKind.Document, List: list, Form: form,
                Parts: parts, Presentation: populated ? new(HasNumber: false) : null));

        var result = Project(McpToolNames.GetMetadata, metadata);

        Assert.Equal(catalog ? "catalog" : "document", result.GetProperty("kind").GetString());
        Assert.Equal("sample.record", result.GetProperty("typeCode").GetString());
        Assert.Equal(populated ? 1 : 0, result.GetProperty("filters").GetArrayLength());
        if (populated)
        {
            Assert.Equal("name", result.GetProperty("fields")[0].GetProperty("key").GetString());
            Assert.Equal("name", result.GetProperty("formFields")[0].GetProperty("key").GetString());
            Assert.Equal("lines", result.GetProperty("parts")[0].GetProperty("partCode").GetString());
            if (!catalog)
                Assert.False(result.GetProperty("hasNumber").GetBoolean());
        }
        else
        {
            Assert.False(result.TryGetProperty("fields", out _));
            Assert.False(result.TryGetProperty("formFields", out _));
        }
    }

    [Fact]
    public void ReportMetadataUsesTheSameDefinitionProjectionAndUnknownMetadataRemainsEmpty()
    {
        var report = new ReportDefinitionDto("sample.report", "Summary");

        Assert.Equal(Project(McpToolNames.GetReportDefinition, report).GetRawText(),
            Project(McpToolNames.GetMetadata, new MetadataResult(Report: report)).GetRawText());
        Assert.Equal("{}", Project(McpToolNames.GetMetadata, new MetadataResult()).GetRawText());
    }

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(10, false, false)]
    [InlineData(0, true, false)]
    public void TypeAndReportCountsArePublishedOnlyForACompleteListing(int offset, bool hasMore, bool complete)
    {
        var metadata = new MetadataPage(
            [new(McpSourceKind.Catalog, "sample.party", "Party"), new(McpSourceKind.Document, "sample.order", "Order")],
            offset, 10, hasMore);
        var reports = new ReportsResult([new("sample.report", "Summary", null, null)], offset, 10, hasMore);

        var types = Project(McpToolNames.ListMetadata, metadata);
        var summaries = Project(McpToolNames.ListReports, reports);

        Assert.Equal(complete, types.TryGetProperty("typeCounts", out var counts));
        Assert.Equal(complete, summaries.TryGetProperty("count", out var count));
        if (complete)
        {
            Assert.Equal(1, counts.GetProperty("catalog").GetInt32());
            Assert.Equal(1, counts.GetProperty("document").GetInt32());
            Assert.Equal(1, count.GetInt32());
        }
        Assert.Equal(hasMore, types.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public void AuditProjectionPreservesActorsAndLegacyValuesWithoutTransportLayout()
    {
        var date = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var page = new AuditLogPageDto(
        [
            new(Guid.NewGuid(), 1, Guid.NewGuid(), "document.posted", new(null, "Operator", null), date, null, "legacy metadata",
            [
                new("amount", "1.25", "2.50"),
                new("memo", "legacy value", null),
                new("updated_at_utc", null, "\"2026-10-20\"")
            ]),
            new(Guid.NewGuid(), 1, Guid.NewGuid(), "draft.created", null, date, null, null, [])
        ], new(date, Guid.NewGuid()), 10);

        var result = Project(McpToolNames.GetAuditHistory, page);

        Assert.False(result.GetProperty("endReached").GetBoolean());
        var first = result.GetProperty("items")[0];
        Assert.True(first.GetProperty("actorRecorded").GetBoolean());
        Assert.Equal("Document Posted", first.GetProperty("action").GetString());
        Assert.Equal("legacy metadata", first.GetProperty("metadata").GetString());
        Assert.Equal(2, first.GetProperty("changes").GetArrayLength());
        Assert.Equal(1.25m, first.GetProperty("changes")[0].GetProperty("before").GetDecimal());
        Assert.Equal("legacy value", first.GetProperty("changes")[1].GetProperty("before").GetString());
        Assert.False(result.GetProperty("items")[1].GetProperty("actorRecorded").GetBoolean());
        Assert.True(Project(McpToolNames.GetAuditHistory, page with { NextCursor = null }).GetProperty("endReached").GetBoolean());
    }

    [Fact]
    public void ReportProjectionKeepsNullCellsAndMismatchedCellTypes()
    {
        var report = new ReportExecutionResponseDto(new(
            [new("amount", "Amount", "decimal")],
            [new(ReportRowKind.Detail, [new(null, "Unavailable", "string")])]), 0, 10, 1, false, null);

        var result = Project(McpToolNames.ExecuteReport, report);

        var cell = result.GetProperty("rows")[0].GetProperty("cells")[0];
        Assert.Equal("Unavailable", cell.GetProperty("display").GetString());
        Assert.Equal("string", cell.GetProperty("valueType").GetString());
        Assert.Equal("full", result.GetProperty("resultView").GetString());
    }

    [Fact]
    public void ReportProjectionPreservesCollapsedGroupsAndMergedCells()
    {
        var report = new ReportExecutionResponseDto(new(
            [new("amount", "Amount", "decimal")],
            [new(ReportRowKind.Group, [new(JsonSerializer.SerializeToElement("Group"), "Group", "string", ColSpan: 2, RowSpan: 3)],
                OutlineLevel: 2, IsExpanded: false)]), 0, 10, 1, false, null);

        var row = Project(McpToolNames.ExecuteReport, report).GetProperty("rows")[0];

        Assert.Equal(2, row.GetProperty("outlineLevel").GetInt32());
        Assert.False(row.GetProperty("isExpanded").GetBoolean());
        var cell = row.GetProperty("cells")[0];
        Assert.Equal(2, cell.GetProperty("colSpan").GetInt32());
        Assert.Equal(3, cell.GetProperty("rowSpan").GetInt32());
        Assert.False(cell.TryGetProperty("display", out _));
    }

    private static JsonElement Project(string tool, object value)
        => McpAssistantProjection.Create(tool, JsonSerializer.SerializeToElement(value, Json), new Dictionary<string, object?>());
}
