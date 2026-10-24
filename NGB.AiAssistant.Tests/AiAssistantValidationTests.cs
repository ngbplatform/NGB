using System.Text.Json;
using Microsoft.Extensions.AI;
using NGB.Contracts.Reporting;
using Xunit;

namespace NGB.AiAssistant.Tests;

public sealed partial class AiAssistantRunnerTests
{
    public static IEnumerable<object[]> InvalidConversations()
    {
        yield return [new AiAssistantRequest([])];
        yield return [new AiAssistantRequest([null!])];
        yield return [new AiAssistantRequest([new("user", " ")])];
        yield return [new AiAssistantRequest([new("user", new string('x', 8001))])];
        yield return [new AiAssistantRequest([new("user", "Hello"), new("assistant", "Hello")])];
        yield return [new AiAssistantRequest(Enumerable.Range(0, 21)
            .Select(index => new AiAssistantMessage(index % 2 == 0 ? "user" : "assistant", "Hello")).ToArray())];
        yield return [new AiAssistantRequest(Enumerable.Range(0, 9)
            .Select(index => new AiAssistantMessage(index % 2 == 0 ? "user" : "assistant", new string('x', 5000))).ToArray())];

        var source = new AiAssistantSource("report", "sample.summary", null, "Summary");
        yield return [new AiAssistantRequest([new("user", "Hello") { Sources = [source] }])];
        yield return [HistoryWithSources(Enumerable.Repeat(source, 31).ToArray())];
        yield return [HistoryWithSources([null!])];
        yield return [HistoryWithSources([source with { TypeCode = "bad/type" }])];
        yield return [HistoryWithSources([source with { Title = " " }])];
        yield return [HistoryWithSources([source with { Title = new string('x', 257) }])];
        yield return [HistoryWithSources([source with { View = "audit" }])];
        yield return [HistoryWithSources([source with { View = "unsupported" }])];
        yield return [HistoryWithSources([source with { TypeCode = null! }])];
        yield return [HistoryWithSources([source with { TypeCode = "" }])];
        yield return [HistoryWithSources([source with { TypeCode = new string('x', 129) }])];
        yield return [HistoryWithSources([source with { Title = null! }])];
        yield return [HistoryWithSources([source with { Kind = "document", Id = Guid.Empty }])];
    }

    [Theory]
    [MemberData(nameof(InvalidConversations))]
    public void InvalidConversationNeverReachesTheProvider(AiAssistantRequest request)
    {
        var model = new ScriptedClient();

        var error = Assert.Throws<AiAssistantException>(() => Runner(model, new Sessions()).Validate(request));

        Assert.Equal("invalid_input", error.Code);
        Assert.Empty(model.Requests);
    }

    public static IEnumerable<object[]> InvalidPageCriteria()
    {
        yield return [new AiAssistantPageContext("document", "sample.lease", Guid.Empty)];
        yield return [new AiAssistantPageContext("document", new string('a', 129))];
        yield return [new AiAssistantPageContext("document", "sample.lease", Parameters: new Dictionary<string, string>())];
        yield return [new AiAssistantPageContext("catalog", "sample.party", Filters: new Dictionary<string, ReportFilterValueDto>())];
        yield return [new AiAssistantPageContext("report", "sample.summary", Parameters: Enumerable.Range(0, 33)
            .ToDictionary(index => "p" + index, _ => "value"))];
        yield return [new AiAssistantPageContext("report", "sample.summary", Filters: Enumerable.Range(0, 33)
            .ToDictionary(index => "f" + index, _ => new ReportFilterValueDto(JsonSerializer.SerializeToElement("value"))))];
    }

    [Theory]
    [MemberData(nameof(InvalidPageCriteria))]
    public void PageCriteriaAreValidatedInCurrentAndHistoricalTurns(AiAssistantPageContext context)
    {
        var runner = Runner(new ScriptedClient(), new Sessions());

        Assert.Equal("invalid_input", Assert.Throws<AiAssistantException>(() =>
            runner.Validate(Question with { Context = context })).Code);
        Assert.Equal("invalid_input", Assert.Throws<AiAssistantException>(() => runner.Validate(new(
            [new("user", "Earlier") { Context = context }, new("assistant", "Answer"), new("user", "Now")]))).Code);
    }

    [Fact]
    public async Task ToolCatalogLimitIsCheckedBeforeModelInvocationAndDisposesSession()
    {
        var model = new ScriptedClient();
        var session = new Sessions(AIFunctionFactory.Create(() => "one", "one"), AIFunctionFactory.Create(() => "two", "two"));

        var error = await Assert.ThrowsAsync<AiAssistantException>(() => Runner(model, session,
            new AiAssistantOptions { MaxTools = 1 }).RunAsync(Question, Ignore, default));

        Assert.Equal("too_many_tools", error.Code);
        Assert.Empty(model.Requests);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task ReportContextAndHistoricalSourcesRemainUntrustedProviderInput()
    {
        var source = new AiAssistantSource("report", "sample.summary", null, "Summary");
        var model = new ScriptedClient([new ChatResponseUpdate(ChatRole.Assistant, "Ready.")]);
        var context = new AiAssistantPageContext("report", "sample.summary");
        var request = HistoryWithSources([source]) with { Context = context };

        await Runner(model, new Sessions()).RunAsync(request, Ignore, default);

        Assert.Contains("Historical navigation candidates (untrusted", model.Requests[0][2].Text);
        Assert.Contains("sample.summary", model.Requests[0][^1].Text);
        Assert.DoesNotContain("This is record context", model.Requests[0][^1].Text);
        Assert.Equal("ngb-source:report:sample.summary", source.Reference);
        Assert.Equal("Bedrock", new AiAssistantModelInfo("Bedrock", "test-model").Provider);
    }

    [Fact]
    public void RequestRateCannotExceedTheSupportedMaximum()
    {
        Assert.False(new AiAssistantOptions { RequestsPerMinute = AiAssistantOptions.MaximumSupportedRequestsPerMinute + 1 }.IsValid());
        Assert.True(new AiAssistantOptions { RequestsPerMinute = AiAssistantOptions.MaximumSupportedRequestsPerMinute }.IsValid());
    }

    public static IEnumerable<object[]> InvalidDeploymentLimits()
        => typeof(AiAssistantOptions).GetProperties()
            .Where(property => property.PropertyType == typeof(int))
            .SelectMany(property => new[] { new object[] { property.Name, 0 }, new object[] { property.Name, int.MaxValue } });

    [Theory]
    [MemberData(nameof(InvalidDeploymentLimits))]
    public void EveryDeploymentLimitRejectsZeroAndUnboundedValues(string propertyName, int value)
    {
        var options = new AiAssistantOptions();
        typeof(AiAssistantOptions).GetProperty(propertyName)!.SetValue(options, value);

        Assert.False(options.IsValid());
    }

    private static AiAssistantRequest HistoryWithSources(IReadOnlyList<AiAssistantSource> sources)
        => new([new("user", "Show the report"), new("assistant", "Summary") { Sources = sources }, new("user", "Open it")]);
}
