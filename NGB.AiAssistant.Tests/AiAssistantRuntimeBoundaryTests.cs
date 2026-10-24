using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace NGB.AiAssistant.Tests;

public sealed partial class AiAssistantRunnerTests
{
    [Theory]
    [InlineData("bytes")]
    [InlineData("characters")]
    [InlineData("updates")]
    public async Task OversizedProviderStreamsFailWithoutPublishingPartialAnswers(string limit)
    {
        var limits = new AiAssistantOptions();
        ChatResponseUpdate[] updates;
        if (limit == "bytes")
            updates = [new(ChatRole.Assistant, [new TextReasoningContent(new string('x', limits.MaxContextBytes + 1))])];
        else if (limit == "characters")
            updates = [new(ChatRole.Assistant, new string('x', limits.MaxOutputCharacters + 1))];
        else
            updates = Enumerable.Range(0, 32_769).Select(_ => new ChatResponseUpdate()).ToArray();

        var model = new ScriptedClient(updates);
        var session = new Sessions();
        var events = new List<AiAssistantEvent>();

        var error = await Assert.ThrowsAsync<AiAssistantException>(() => Runner(model, session, limits)
            .RunAsync(Question, Capture(events), default));

        Assert.Equal("response_limit", error.Code);
        Assert.DoesNotContain(events, item => item.Type is "text" or "done");
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task PublishedTextChunksDoNotSplitASurrogatePair()
    {
        var answer = new string('x', 2047) + "😀" + new string('y', 100);
        var model = new ScriptedClient([new ChatResponseUpdate(ChatRole.Assistant, answer)]);
        var events = new List<AiAssistantEvent>();

        await Runner(model, new Sessions()).RunAsync(Question, Capture(events), default);

        var chunks = events.Where(item => item.Type == "text").Select(item => item.Text!).ToArray();
        Assert.Equal(answer, string.Concat(chunks));
        Assert.Equal(2, chunks.Length);
        Assert.All(chunks, chunk => Assert.False(char.IsHighSurrogate(chunk[^1])));
        Assert.StartsWith("😀", chunks[1]);
    }

    [Fact]
    public async Task UsageMetadataDoesNotBecomeAnswerText()
    {
        var model = new ScriptedClient([new ChatResponseUpdate(ChatRole.Assistant,
        [
            new TextReasoningContent(null),
            new TextContent("Ready."),
            new UsageContent(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 2 })
        ])]);
        var events = new List<AiAssistantEvent>();

        await Runner(model, new Sessions()).RunAsync(Question, Capture(events), default);

        Assert.Equal("Ready.", string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text)));
        Assert.Equal("done", events[^1].Type);
    }

    [Fact]
    public async Task RepeatedTruncationWithReasoningDisabledStopsRecovery()
    {
        ChatResponseUpdate[] truncated = [new(ChatRole.Assistant, [new TextReasoningContent("Unfinished")])
        {
            FinishReason = ChatFinishReason.Length
        }];
        var model = new ScriptedClient(truncated, truncated);

        var error = await Assert.ThrowsAsync<AiAssistantException>(() => Runner(model, new Sessions())
            .RunAsync(Question, Ignore, default));

        Assert.Equal("response_limit", error.Code);
        Assert.Equal(ReasoningEffort.None, model.Options[^1].Reasoning?.Effort);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task HistoricalSourceResolutionStopsAtThePublishedSourceLimit()
    {
        var sources = Enumerable.Range(0, 31)
            .Select(index => new AiAssistantSource("document", "sample.record", Guid.NewGuid(), "Record " + index)).ToArray();
        var request = new AiAssistantRequest(
        [
            new("user", "First page"),
            new("assistant", "Records") { Sources = sources[..30] },
            new("user", "Next page"),
            new("assistant", "Records") { Sources = sources[30..] },
            new("user", "Show the links")
        ]);
        var model = new ScriptedClient([new ChatResponseUpdate(ChatRole.Assistant, string.Join(' ', sources.Select(source => source.Reference)))]);
        var session = new ResolvingSourceSession();
        var runner = new AiAssistantRunner(model, session, Options.Create(new AiAssistantOptions { MaxToolCalls = 64 }));
        var events = new List<AiAssistantEvent>();

        await runner.RunAsync(request, Capture(events), default);

        Assert.Equal(30, session.Resolved);
        Assert.Equal(30, events.Count(item => item.Type == "source"));
    }

    [Fact]
    public async Task HistoricalResolutionSkipsUnreferencedAndAlreadyVerifiedSources()
    {
        var verified = new AiAssistantSource("document", "sample.record", Guid.NewGuid(), "Current record");
        var requested = verified with { Id = Guid.NewGuid(), Title = "Requested record" };
        var unused = verified with { Id = Guid.NewGuid(), Title = "Unused record" };
        var session = new ResolvingSourceSession(new SourceTool(new(new { }, [verified])));
        var model = new ScriptedClient(
            [new(ChatRole.Assistant, [new FunctionCallContent("current", "read", null)])],
            [new(ChatRole.Assistant, verified.Reference + " " + requested.Reference)]);
        var runner = new AiAssistantRunner(model, session, Options.Create(new AiAssistantOptions()));
        var events = new List<AiAssistantEvent>();

        await runner.RunAsync(HistoryWithSources([verified, requested, unused]), Capture(events), default);

        Assert.Equal(1, session.Resolved);
        Assert.Equal(new[] { verified, requested }, events.Where(item => item.Type == "source").Select(item => item.Source));
    }

    private sealed class ResolvingSourceSession(params AIFunction[] tools)
        : IAiAssistantToolSessionFactory, IAiAssistantToolSession, IAiAssistantSourceResolver
    {
        public IReadOnlyList<AIFunction> Tools => tools;
        internal int Resolved { get; private set; }
        public Task<IAiAssistantToolSession> OpenAsync(CancellationToken ct) => Task.FromResult<IAiAssistantToolSession>(this);

        public Task<AiAssistantSource?> ResolveAsync(AiAssistantSource candidate, CancellationToken ct)
        {
            Resolved++;
            return Task.FromResult<AiAssistantSource?>(candidate);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task DisplayReferencesStillRespectTheFinalOutputCharacterLimit()
    {
        var model = new ScriptedClient(
            [new(ChatRole.Assistant, [new FunctionCallContent("date", "read", null)])],
            [new(ChatRole.Assistant, "2026-10-20")]);
        var events = new List<AiAssistantEvent>();
        var tool = new SourceTool(new(new { date = "2026-10-20" }, []));

        var error = await Assert.ThrowsAsync<AiAssistantException>(() => Runner(model, new Sessions(tool),
            new() { MaxOutputCharacters = 10 }).RunAsync(Question, Capture(events), default));

        Assert.Equal("response_limit", error.Code);
        Assert.DoesNotContain(events, item => item.Type is "text" or "done");
    }

    [Fact]
    public async Task AProviderBatchCannotExceedTheReadBudget()
    {
        var reads = 0;
        var tool = AIFunctionFactory.Create((int id) => new { id, count = ++reads }, "read");
        var model = new ScriptedClient(
            [new(ChatRole.Assistant,
            [
                new FunctionCallContent("first", "read", new Dictionary<string, object?> { ["id"] = 1 }),
                new FunctionCallContent("second", "read", new Dictionary<string, object?> { ["id"] = 2 })
            ])],
            [new(ChatRole.Assistant, "Only the first read was available.")]);

        await Runner(model, new Sessions(tool), new() { MaxToolCalls = 1 }).RunAsync(Question, Ignore, default);

        Assert.Equal(1, reads);
        Assert.Contains("read_budget_exhausted", model.Requests[^1][^1].Text);
        Assert.Empty(model.Options[^1].Tools!);
    }

    [Fact]
    public async Task OversizedErrorsSupplyNoEvidenceOrSourceAndDoNotBreakRecovery()
    {
        var source = new AiAssistantSource("report", "sample.summary", null, "Summary");
        var tool = new SourceTool(new(new { error = new string('x', 2048) }, [source], IsError: true));
        var model = new ScriptedClient(
            [new(ChatRole.Assistant, [new FunctionCallContent("read", "read", null)])],
            [new(ChatRole.Assistant, "The report is unavailable.")]);
        var events = new List<AiAssistantEvent>();
        var runner = new AiAssistantRunner(model, new Sessions(tool), Options.Create(new AiAssistantOptions
        {
            MaxToolResultBytes = 1024
        }), logger: NullLogger<AiAssistantRunner>.Instance);

        await runner.RunAsync(Question, Capture(events), default);

        Assert.DoesNotContain(events, item => item.Type is "source" or "value");
        Assert.Contains("result_too_large", System.Text.Json.JsonSerializer.Serialize(model.Requests[^1]));
        Assert.Equal("done", events[^1].Type);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("different")]
    [InlineData("budget")]
    public async Task HistoricalSourcesRequireAValidMatchingResolutionWithinTheBudget(string failure)
    {
        var source = new AiAssistantSource("document", "sample.record", Guid.NewGuid(), "Record");
        var resolved = failure == "invalid" ? source with { Title = " " } : source with { Id = Guid.NewGuid() };
        var sessions = new ResolvingSessions(resolved);
        var model = new ScriptedClient([new ChatResponseUpdate(ChatRole.Assistant, source.Reference)]);
        var runner = new AiAssistantRunner(model, sessions, Options.Create(new AiAssistantOptions
        {
            MaxToolCalls = failure == "budget" ? 0 : 12
        }));
        var events = new List<AiAssistantEvent>();

        await runner.RunAsync(HistoryWithSources([source]), Capture(events), default);

        Assert.DoesNotContain(events, item => item.Type == "source");
        Assert.Equal(failure == "budget" ? 0 : 1, sessions.Candidates.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BlankPageTypeCodesAreRejected(string typeCode)
    {
        Assert.Equal("invalid_input", Assert.Throws<AiAssistantException>(() => Runner(new ScriptedClient(), new Sessions())
            .Validate(Question with { Context = new("document", typeCode) })).Code);
    }

    [Fact]
    public void SupportedIdentifiersAcceptAllDocumentedSeparators()
    {
        Runner(new ScriptedClient(), new Sessions()).Validate(Question with { Context = new("document", "sample_test-record") });
        Assert.True(AiAssistantSources.IsValid(new("document", "sample_test-record", Guid.NewGuid(), "Record")));
        Assert.False(AiAssistantSources.IsValid(new("document", "sample.record", Guid.NewGuid(), "")));
        Assert.False(new AiAssistantOptions { MaxOutputCharacters = AiAssistantOptions.MaximumSupportedContextBytes + 1 }.IsValid());
        Assert.Equal("invalid_input", Assert.Throws<AiAssistantException>(() => Runner(new ScriptedClient(), new Sessions())
            .Validate(Question with { TimeZone = new string('x', 129) })).Code);
    }
}
