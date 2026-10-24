using System.Text.Json;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed class AssistantContinuationBoundaryTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"request\":null}")]
    [InlineData("{\"request\":{}}")]
    [InlineData("{\"request\":{\"cursor\":null}}")]
    [InlineData("{\"request\":{\"cursor\":42}}")]
    [InlineData("{\"request\":{\"cursor\":\" \"}}")]
    public void RequestsWithoutAStringContinuationRemainForApplicationValidation(string json)
    {
        var continuations = new AssistantReportContinuations();
        var arguments = Arguments(json);

        Assert.True(continuations.TryResolve(arguments, out var resolved));
        Assert.Same(arguments, resolved);
    }

    [Theory]
    [InlineData("{\"nextCursor\":null}")]
    [InlineData("{\"nextCursor\":42}")]
    [InlineData("{\"nextCursor\":\"\"}")]
    [InlineData("{\"criteria\":{\"request\":{\"cursor\":\"private\",\"limit\":10}}}")]
    public void PublicationPreservesResultsWithoutCursorsAndRemovesNativeCursorCopies(string json)
    {
        var continuations = new AssistantReportContinuations();

        var result = continuations.Publish(JsonSerializer.Deserialize<JsonElement>(json), Arguments("{}"));

        Assert.DoesNotContain("private", result.GetRawText());
        if (result.TryGetProperty("nextCursor", out var cursor))
            Assert.Equal(JsonSerializer.Deserialize<JsonElement>(json).GetProperty("nextCursor").GetRawText(), cursor.GetRawText());
    }

    [Theory]
    [InlineData("full", "full", true)]
    [InlineData(null, "full", true)]
    [InlineData("totals", "totals", true)]
    [InlineData("totals", "full", false)]
    [InlineData("full", "totals", false)]
    public void ContinuationsPreserveTheirOriginalEvidenceView(string? initial, string proposed, bool valid)
    {
        var continuations = new AssistantReportContinuations();
        var original = Arguments("{\"reportCode\":\"sample.report\",\"request\":{\"limit\":10}}");
        if (initial is not null)
            original["resultView"] = initial;
        var cursor = Publish(continuations, original);
        var next = new Dictionary<string, object?>
        {
            ["reportCode"] = "sample.report",
            ["resultView"] = proposed,
            ["request"] = new { cursor }
        };

        Assert.Equal(valid, continuations.TryResolve(next, out var resolved));
        if (valid)
            Assert.Equal("native-cursor", JsonSerializer.SerializeToElement(resolved).GetProperty("request").GetProperty("cursor").GetString());
    }

    [Theory]
    [InlineData("null", 10)]
    [InlineData("5", 5)]
    [InlineData("20", 10)]
    [InlineData("0", 0)]
    [InlineData("201", 201)]
    [InlineData("\"invalid\"", null)]
    [InlineData("{}", null)]
    public void ContinuationLimitsNeverGrowAValidatedPageAndKeepInvalidInputsForValidation(string limit, int? expected)
    {
        var continuations = new AssistantReportContinuations();
        var cursor = Publish(continuations, Arguments("{\"reportCode\":\"sample.report\",\"request\":{\"limit\":10}}"));
        var next = Arguments($$$"""{"reportCode":"sample.report","request":{"cursor":"{{{cursor}}}","limit":{{{limit}}},"offset":50,"parameters":null}}""");

        Assert.True(continuations.TryResolve(next, out var resolved));

        var request = JsonSerializer.SerializeToElement(resolved).GetProperty("request");
        Assert.False(request.TryGetProperty("offset", out _));
        if (expected is not null)
            Assert.Equal(expected, request.GetProperty("limit").GetInt32());
        else
            Assert.Equal(limit, request.GetProperty("limit").GetRawText());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"limit\":\"invalid\"}")]
    public void ContinuationAcceptsANewPageSizeWhenNoNumericSizeWasSaved(string request)
    {
        var continuations = new AssistantReportContinuations();
        var cursor = Publish(continuations, Arguments($$$"""{"reportCode":"sample.report","request":{{{request}}}}"""));
        var next = Arguments($$$"""{"reportCode":"sample.report","request":{"cursor":"{{{cursor}}}","limit":5}}""");

        Assert.True(continuations.TryResolve(next, out var resolved));
        Assert.Equal(5, JsonSerializer.SerializeToElement(resolved).GetProperty("request").GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData("other.report", false)]
    [InlineData("sample.report", true)]
    public void ContinuationsRejectChangedReportsAndCriteria(string code, bool changeCriteria)
    {
        var continuations = new AssistantReportContinuations();
        var cursor = Publish(continuations, Arguments("{\"reportCode\":\"sample.report\",\"request\":{\"parameters\":{\"date\":\"2026-10-20\"}}}"));
        var next = new Dictionary<string, object?>
        {
            ["reportCode"] = code,
            ["request"] = new { cursor, parameters = changeCriteria ? new { date = "2026-10-21" } : null }
        };

        Assert.False(continuations.TryResolve(next, out _));
        Assert.False(new AssistantReportContinuations().TryResolve(next, out _));
    }

    private static Dictionary<string, object?> Arguments(string json)
        => JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;

    private static string Publish(AssistantReportContinuations continuations, Dictionary<string, object?> arguments)
        => continuations.Publish(JsonSerializer.SerializeToElement(new { nextCursor = "native-cursor" }), arguments)
            .GetProperty("nextCursor").GetString()!;
}
