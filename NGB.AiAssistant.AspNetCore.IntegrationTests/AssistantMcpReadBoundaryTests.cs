using System.Text.Json;
using Microsoft.Extensions.AI;
using NGB.Mcp;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantMcpTransportTests
{
    [Fact]
    public async Task RecordListsRequireMetadataForTheExactKindAndType()
    {
        await using var fixture = await TransportFixture.CreateAsync();
        await using var session = await fixture.Factory.OpenAsync(default);
        var list = session.Tools.Single(tool => tool.Name == McpToolNames.ListRecords);
        var arguments = new AIFunctionArguments { ["kind"] = "catalog", ["typeCode"] = "sample.party" };

        var denied = Assert.IsType<AiAssistantToolResult>(await list.InvokeAsync(arguments));

        Assert.True(denied.IsError);
        Assert.Contains("metadata_required", JsonSerializer.Serialize(denied.Data));
        Assert.Empty(fixture.Handler.Calls);
        fixture.Handler.ToolResult = Envelope(new { });
        await session.Tools.Single(tool => tool.Name == McpToolNames.GetMetadata).InvokeAsync(arguments);
        var allowed = Assert.IsType<AiAssistantToolResult>(await list.InvokeAsync(arguments));
        Assert.False(allowed.IsError);
        Assert.Equal(2, fixture.Handler.Calls.Count);
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"parent\":null}", false)]
    [InlineData("{\"parent\":42}", false)]
    [InlineData("{\"parent\":\" \"}", false)]
    [InlineData("{\"parent\":\"selected\"}", true)]
    public async Task ContinuationRequiresANonemptyDeclaredSelection(string filters, bool allowed)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        fixture.Handler.ToolResult = Envelope(new { });
        await using var session = await fixture.Factory.OpenAsync(default);
        var arguments = new AIFunctionArguments
        {
            ["kind"] = "catalog", ["typeCode"] = "sample.party",
            ["cursor"] = "filtered-page", ["filters"] = JsonSerializer.Deserialize<JsonElement>(filters)
        };
        await session.Tools.Single(tool => tool.Name == McpToolNames.GetMetadata).InvokeAsync(arguments);

        var result = Assert.IsType<AiAssistantToolResult>(await session.Tools.Single(tool => tool.Name == McpToolNames.ListRecords)
            .InvokeAsync(arguments));

        Assert.Equal(!allowed, result.IsError);
        Assert.Equal(allowed ? 2 : 1, fixture.Handler.Calls.Count);
        if (!allowed)
            Assert.Contains("filter_required", JsonSerializer.Serialize(result.Data));
    }

    [Theory]
    [InlineData("catalogs")]
    [InlineData("documents")]
    [InlineData("generalJournalEntries")]
    public async Task UnfilteredCursorsCannotBeReusedWithADifferentSearch(string section)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        fixture.Handler.ToolResult = Envelope(new { });
        await using var session = await fixture.Factory.OpenAsync(default);
        var arguments = new AIFunctionArguments { ["kind"] = "document", ["typeCode"] = "sample.record" };
        await session.Tools.Single(tool => tool.Name == McpToolNames.GetMetadata).InvokeAsync(arguments);
        fixture.Handler.ToolResult = Envelope(new Dictionary<string, object?>
        {
            [section] = new { items = Array.Empty<object>(), offset = 0, limit = 10, hasMore = true, nextCursor = "next-page" }
        });
        var list = session.Tools.Single(tool => tool.Name == McpToolNames.ListRecords);
        await list.InvokeAsync(arguments);
        arguments["cursor"] = "next-page";
        arguments["search"] = "invented selection";

        var result = Assert.IsType<AiAssistantToolResult>(await list.InvokeAsync(arguments));

        Assert.Equal("unfiltered_scan", result.ErrorCode);
        Assert.Equal(2, fixture.Handler.Calls.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NullAndMissingBusinessDataAreNotReportedAsAnEmptyList(bool explicitNull)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        fixture.Handler.ToolResult = explicitNull ? Envelope(null) : new { content = Array.Empty<object>(), structuredContent = new { schemaVersion = 1 } };
        await using var session = await fixture.Factory.OpenAsync(default);

        var result = Assert.IsType<AiAssistantToolResult>(await session.Tools.Single(tool => tool.Name == "custom_read").InvokeAsync(new()));

        Assert.True(result.IsError);
        Assert.Contains("empty_result", JsonSerializer.Serialize(result.Data));
        Assert.Empty(result.Sources);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task SourceResolutionUsesAuthorizedReportOrRecordReads(bool report, bool denied)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        fixture.Handler.Tools.Add(ProtocolHandler.Tool(report ? McpToolNames.GetReportDefinition : McpToolNames.GetRecord));
        var source = new AiAssistantSource(report ? "report" : "document", "sample.source", report ? null : Guid.NewGuid(), "Source");
        fixture.Handler.ToolResult = denied
            ? new { isError = true, content = new[] { new { type = "text", text = "Forbidden" } } }
            : new
            {
                content = Array.Empty<object>(),
                structuredContent = new
                {
                    schemaVersion = 1,
                    data = new { reportCode = "sample.source", name = "Source" },
                    sources = new[] { new { source.Kind, source.TypeCode, source.Id, source.Title } }
                }
            };
        await using var session = await fixture.Factory.OpenAsync(default);

        var resolved = await ((IAiAssistantSourceResolver)session).ResolveAsync(source, default);

        Assert.Equal(denied ? null : source, resolved);
        var call = Assert.Single(fixture.Handler.Calls);
        Assert.Equal(report ? McpToolNames.GetReportDefinition : McpToolNames.GetRecord, call.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ExplicitNonCompatibilityMetadataKeepsAReadToolAvailable()
    {
        await using var fixture = await TransportFixture.CreateAsync();
        fixture.Handler.Tools.Add(ProtocolHandler.Tool("documented_read", true, false));
        fixture.Handler.Tools.Add(new
        {
            name = "read_with_other_metadata",
            description = "Read data with unrelated protocol metadata.",
            inputSchema = new { type = "object", properties = new { } },
            annotations = new { readOnlyHint = true },
            _meta = new { version = "1" }
        });
        await using var session = await fixture.Factory.OpenAsync(default);

        Assert.Contains(session.Tools, tool => tool.Name == "documented_read");
        Assert.Contains(session.Tools, tool => tool.Name == "read_with_other_metadata");
    }

    [Theory]
    [InlineData("catalogs")]
    [InlineData("documents")]
    [InlineData("generalJournalEntries")]
    public async Task CompletedRecordPagesDoNotRequireAContinuation(string section)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        fixture.Handler.ToolResult = Envelope(new { });
        await using var session = await fixture.Factory.OpenAsync(default);
        var arguments = new AIFunctionArguments { ["kind"] = "catalog", ["typeCode"] = "sample.record" };
        await session.Tools.Single(tool => tool.Name == McpToolNames.GetMetadata).InvokeAsync(arguments);
        fixture.Handler.ToolResult = Envelope(new Dictionary<string, object?>
        {
            [section] = new { items = Array.Empty<object>(), offset = 0, limit = 10, hasMore = false, nextCursor = (string?)null }
        });

        var result = Assert.IsType<AiAssistantToolResult>(await session.Tools.Single(tool => tool.Name == McpToolNames.ListRecords)
            .InvokeAsync(arguments));

        Assert.False(result.IsError);
        Assert.False(fixture.Handler.Calls[^1].GetProperty("arguments").TryGetProperty("cursor", out _));
    }

    [Theory]
    [InlineData("full")]
    [InlineData("totals")]
    public async Task OversizedReportPagesReduceTheConfiguredDefaultWhenNoLimitWasSpecified(string view)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        await using var session = await fixture.Factory.OpenAsync(default);
        var arguments = new AIFunctionArguments { ["request"] = new { }, ["resultView"] = view };
        var tool = Assert.IsAssignableFrom<IAiAssistantPagedTool>(session.Tools.Single(tool => tool.Name == McpToolNames.ExecuteReport));

        Assert.True(tool.TryReducePage(arguments, out var reduced));

        var originalLimit = view == "totals" ? McpReadOptions.MaximumReportPageSize : new AiAssistantHttpOptions().ReportPageSize;
        Assert.Equal(originalLimit / 2, JsonSerializer.SerializeToElement(reduced["request"]).GetProperty("limit").GetInt32());
        Assert.Equal(view, reduced["resultView"]);
        Assert.Empty(fixture.Handler.Calls);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("typeCode")]
    public async Task MissingRecordSelectionIsPassedToMcpForSchemaValidation(string missing)
    {
        await using var fixture = await TransportFixture.CreateAsync();
        fixture.Handler.ToolResult = Envelope(new { });
        await using var session = await fixture.Factory.OpenAsync(default);
        var arguments = new AIFunctionArguments { ["kind"] = "catalog", ["typeCode"] = "sample.party" };
        arguments.Remove(missing);

        await session.Tools.Single(tool => tool.Name == McpToolNames.GetMetadata).InvokeAsync(arguments);
        await session.Tools.Single(tool => tool.Name == McpToolNames.ListRecords).InvokeAsync(arguments);

        Assert.Equal(2, fixture.Handler.Calls.Count);
    }

    private static object Envelope(object? data)
        => new { content = Array.Empty<object>(), structuredContent = new { schemaVersion = 1, data, sources = Array.Empty<object>() } };
}
