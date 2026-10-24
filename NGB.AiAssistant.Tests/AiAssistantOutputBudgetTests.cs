using Microsoft.Extensions.AI;
using Xunit;

namespace NGB.AiAssistant.Tests;

public sealed partial class AiAssistantRunnerTests
{
    [Theory]
    [InlineData(256, 1024)]
    [InlineData(1024, 256)]
    public async Task InitialGenerationHonorsTheConfiguredHardCeiling(int initial, int maximum)
    {
        var model = new ScriptedClient([new ChatResponseUpdate(ChatRole.Assistant, "Ready.")]);
        var limits = new AiAssistantOptions { InitialOutputTokens = initial, MaxOutputTokens = maximum };

        await Runner(model, new Sessions(), limits).RunAsync(Question, Ignore, default);

        Assert.Equal(Math.Min(initial, maximum), Assert.Single(model.Options).MaxOutputTokens);
    }

    [Fact]
    public async Task TruncatedAnswerRecoversWithMoreTokensAndTheSameEvidence()
    {
        var reads = 0;
        var limits = new AiAssistantOptions { InitialOutputTokens = 256, MaxOutputTokens = 1024 };
        var model = new ScriptedClient(
            [new(ChatRole.Assistant, [new FunctionCallContent("read", "read", null)])],
            [new(ChatRole.Assistant, "Truncated draft") { FinishReason = ChatFinishReason.Length }],
            [new(ChatRole.Assistant, "Outstanding: 1937.5678.")]);
        var events = new List<AiAssistantEvent>();
        var tool = AIFunctionFactory.Create(() => new { amount = 1937.5678m, reads = ++reads }, "read");

        await Runner(model, new Sessions(tool), limits).RunAsync(Question, Capture(events), default);

        Assert.Equal(1, reads);
        Assert.Equal(new int?[] { 256, 256, 1024 }, model.Options.Select(option => option.MaxOutputTokens));
        Assert.Empty(model.Options[^1].Tools!);
        Assert.Contains("1937.5678", model.Requests[^1][^1].Text);
        Assert.DoesNotContain("Truncated draft", model.Requests[^1][^1].Text);
        Assert.Equal("Outstanding: 1937.5678.", string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text)));
        Assert.Equal("done", events[^1].Type);
    }

    [Fact]
    public async Task KnownFinalAnswerUsesTheFullBudgetWithoutFirstTruncatingADraft()
    {
        var limits = new AiAssistantOptions { InitialOutputTokens = 256, MaxOutputTokens = 1024, MaxToolCalls = 1 };
        var model = new ScriptedClient(
            [new(ChatRole.Assistant, [new FunctionCallContent("read", "read", null)])],
            [new(ChatRole.Assistant, "Seven records.")]);

        await Runner(model, new Sessions(AIFunctionFactory.Create(() => new { count = 7 }, "read")), limits)
            .RunAsync(Question, Ignore, default);

        Assert.Equal(new int?[] { 256, 1024 }, model.Options.Select(option => option.MaxOutputTokens));
        Assert.Empty(model.Options[^1].Tools!);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(1024)]
    public async Task HardOutputLimitDoesNotRepeatAnIdenticalRequestOrPublishAPartialAnswer(int initial)
    {
        var limits = new AiAssistantOptions { InitialOutputTokens = initial, MaxOutputTokens = 1024 };
        ChatResponseUpdate[] truncated = [new(ChatRole.Assistant, "Unfinished") { FinishReason = ChatFinishReason.Length }];
        var model = new ScriptedClient(truncated, truncated);
        var events = new List<AiAssistantEvent>();

        var error = await Assert.ThrowsAsync<AiAssistantException>(() => Runner(model, new Sessions(), limits)
            .RunAsync(Question, Capture(events), default));

        Assert.Equal("response_limit", error.Code);
        Assert.Equal(initial < limits.MaxOutputTokens ? 2 : 1, model.Requests.Count);
        Assert.All(model.Options, option => Assert.True(option.MaxOutputTokens <= limits.MaxOutputTokens));
        Assert.DoesNotContain(events, item => item.Type is "text" or "done");
    }

    [Fact]
    public async Task ReasoningCanRecoverAtTheHardCeilingWithoutIncreasingIt()
    {
        var limits = new AiAssistantOptions { InitialOutputTokens = 256, MaxOutputTokens = 256 };
        var model = new ScriptedClient(
            [new(ChatRole.Assistant, [new TextReasoningContent("Unfinished reasoning")]) { FinishReason = ChatFinishReason.Length }],
            [new(ChatRole.Assistant, "Ready.")]);

        await Runner(model, new Sessions(), limits).RunAsync(Question, Ignore, default);

        Assert.Equal(2, model.Requests.Count);
        Assert.All(model.Options, option => Assert.Equal(limits.MaxOutputTokens, option.MaxOutputTokens));
        Assert.Equal(ReasoningEffort.None, model.Options[^1].Reasoning?.Effort);
    }

    [Fact]
    public async Task CancellationBeforeRecoveryPreventsAnotherProviderRequest()
    {
        using var cancellation = new CancellationTokenSource();
        var model = new ScriptedClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "Truncated") { FinishReason = ChatFinishReason.Length }]);
        var events = new List<AiAssistantEvent>();
        var statuses = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(model, new Sessions()).RunAsync(Question,
            (item, _) =>
            {
                events.Add(item);
                if (item.Type == "status" && item.Text == "Preparing an answer…" && ++statuses == 2)
                    cancellation.Cancel();

                return ValueTask.CompletedTask;
            }, cancellation.Token));

        Assert.Single(model.Requests);
        Assert.DoesNotContain(events, item => item.Type is "text" or "done");
    }

    [Theory]
    [InlineData(0, 1024)]
    [InlineData(-1, 1024)]
    [InlineData(AiAssistantOptions.MaximumSupportedOutputTokens + 1, 1024)]
    [InlineData(256, 0)]
    [InlineData(256, AiAssistantOptions.MaximumSupportedOutputTokens + 1)]
    public void InvalidOutputBudgetsAreRejected(int initial, int maximum)
    {
        Assert.False(new AiAssistantOptions { InitialOutputTokens = initial, MaxOutputTokens = maximum }.IsValid());
    }
}
