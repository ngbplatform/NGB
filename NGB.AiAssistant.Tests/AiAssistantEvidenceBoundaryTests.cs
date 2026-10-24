using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Xunit;

namespace NGB.AiAssistant.Tests;

public sealed class AiAssistantEvidenceBoundaryTests
{
    [Theory]
    [InlineData("-USD12.50", "-12.50", "USD", true)]
    [InlineData("+USD12.50", "12.50", "USD", true)]
    [InlineData("−USD12.50", "-12.50", "USD", true)]
    [InlineData("12.50 XYZ", "12.50", null, true)]
    [InlineData("12.50 $", "12.50", "$", true)]
    [InlineData("12.50 £", "12.50", "$", false)]
    [InlineData("12.50%", "0.125", null, true)]
    [InlineData("12.50%", "0.25", null, false)]
    [InlineData("12.50 (exact: 12.501)", "12.501", null, true)]
    [InlineData("12.50 (exact: 12.601)", "12.601", null, false)]
    [InlineData("1.2.3", "12.50", null, true)]
    [InlineData("99999999999999999999999999999999999.99", "12.50", null, true)]
    [InlineData("12.50", "12.50", "x", true)]
    public void NumericEvidenceChecksSignsCurrenciesPrecisionAndOverflow(
        string answer, string amount, string? currency, bool supported)
    {
        var evidence = new AiAssistantNumericEvidence();
        evidence.Add(new { amount, currency, enabled = true, missing = (object?)null, label = "plain text", shortLabel = "abc" });
        evidence.Add(JsonSerializer.Deserialize<JsonElement>("1e100"));

        Assert.Equal(supported, evidence.Correction(answer) is null);
    }

    [Fact]
    public void CanonicalArgumentsIgnoreObjectOrderButPreserveArrayOrderAndNulls()
    {
        var first = new Dictionary<string, object?> { ["rows"] = new object?[] { new { b = 2, a = 1 }, null, true } };
        var second = new Dictionary<string, object?> { ["rows"] = new object?[] { new { a = 1, b = 2 }, null, true } };
        var reversed = new Dictionary<string, object?> { ["rows"] = new object?[] { true, null, new { a = 1, b = 2 } } };

        Assert.Equal(AiAssistantContextBudget.CallKey("read", first), AiAssistantContextBudget.CallKey("read", second));
        Assert.NotEqual(AiAssistantContextBudget.CallKey("read", first), AiAssistantContextBudget.CallKey("read", reversed));
        Assert.Equal(AiAssistantContextBudget.CallKey("read", null),
            AiAssistantContextBudget.CallKey("read", new Dictionary<string, object?>()));
    }

    [Fact]
    public void OversizedPlainEvidenceDoesNotCreateDisplayValues()
    {
        var values = new AiAssistantValues();

        Assert.False(values.TryPrepare(new { count = 123456789 }, 1, out var prepared));

        Assert.Null(prepared);
        Assert.Empty(values.Items);
    }

    [Fact]
    public void AFinalBatchRetainsItsLastResultWhenEarlierResultsMustBeOmitted()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Instructions"),
            new(ChatRole.User, "Question"),
            new(ChatRole.Tool,
            [
                new FunctionResultContent("old", new string('x', 8000)),
                new FunctionResultContent("latest", new string('y', 8000))
            ])
        };

        Assert.Null(AiAssistantContextBudget.FitFinalAnswer(messages, 2000));
        Assert.Contains("evidence_omitted", System.Text.Json.JsonSerializer.Serialize(messages));
        Assert.Equal(new string('y', 8000), Assert.IsType<FunctionResultContent>(messages[^1].Contents[^1]).Result);
    }

    [Fact]
    public void FinalAnswerRetainsUnpairedEvidenceAndOmitsOnlyOlderOversizedResults()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Instructions"),
            new(ChatRole.User, "Question"),
            new(ChatRole.Tool, [new TextContent("annotation"), new FunctionResultContent("old", new string('x', 8000))]),
            new(ChatRole.Tool, [new FunctionResultContent("latest", "Verified latest evidence")])
        };

        var answer = AiAssistantContextBudget.FitFinalAnswer(messages, 2000);

        Assert.NotNull(answer);
        Assert.Contains("evidence_omitted", answer[^1].Text);
        Assert.Contains("Verified latest evidence", answer[^1].Text);
        Assert.DoesNotContain(new string('x', 100), answer[^1].Text);
        Assert.Contains("\"operation\":null", answer[^1].Text);
    }

    [Fact]
    public void ValueBudgetRetainsEarlierValuesWhenLaterEvidenceDoesNotFit()
    {
        var values = new AiAssistantValues();
        Assert.True(values.TryPrepare(new { date = "2026-10-20" }, 4096, out _));
        var original = Assert.Single(values.Items);

        Assert.False(values.TryPrepare(new { date = "2026-10-20", other = "2026-10-21", text = new string('x', 1000) }, 100, out var omitted));

        Assert.Null(omitted);
        Assert.Equal(original, Assert.Single(values.Items));
        Assert.Equal(original.Reference, values.Bind("October 20, 2026"));
    }

    [Fact]
    public void DisplayValueLimitLeavesAdditionalSourceValuesLiteral()
    {
        var values = new AiAssistantValues();
        var dates = Enumerable.Range(0, 513).Select(index => new DateOnly(2020, 1, 1).AddDays(index).ToString("yyyy-MM-dd")).ToArray();

        Assert.True(values.TryPrepare(dates, 256 * 1024, out var prepared));

        Assert.Equal(512, values.Items.Count);
        Assert.IsType<JsonObject>(prepared![511]);
        Assert.Equal(dates[512], prepared[512]!.GetValue<string>());
        Assert.Equal(dates[512], values.Bind(dates[512]));
    }

    [Theory]
    [InlineData("broken-2026-99-99.txt")]
    [InlineData("2026-10-20T99:99:99Z")]
    [InlineData("2026-10-20T12:00:00")]
    [InlineData("1234567890123456789012345")]
    public void InvalidDatesArePreservedWithoutInventingAnInstant(string literal)
    {
        var values = new AiAssistantValues();

        Assert.True(values.TryPrepare(new { description = literal }, 4096, out var prepared));

        Assert.Equal(literal, prepared!["description"]!.GetValue<string>());
        Assert.DoesNotContain(values.Items, value => value.Kind is "date" or "timestamp");
        Assert.Null(values.Correction(values.Bind(literal)));
    }

    [Fact]
    public void DefaultTimeZoneAndByteValuesUseExactSourceValues()
    {
        var values = new AiAssistantValues();
        var data = new
        {
            timestamp = "2026-10-20T12:00:00+02:00",
            files = new object[] { new { sizeBytes = 1234L }, new { sizeBytes = -1L }, new { sizeBytes = true } },
            memo = "",
            tooLong = new string('x', 8001)
        };

        Assert.True(values.TryPrepare(data, 16384, out var prepared));

        Assert.Equal("UTC", prepared!["timestamp"]!["displayTimeZone"]!.GetValue<string>());
        Assert.Equal("10/20/2026, 10:00:00 AM", prepared["timestamp"]!["formatted"]!.GetValue<string>());
        var bytes = Assert.Single(values.Items, value => value.Kind == "bytes");
        Assert.Equal(bytes.Reference, values.Bind("1,234 bytes"));
        Assert.Equal(bytes.Reference, values.Bind("1234 bytes"));
        Assert.Equal(-1, prepared["files"]![1]!["sizeBytes"]!.GetValue<int>());
        Assert.True(prepared["files"]![2]!["sizeBytes"]!.GetValue<bool>());
        Assert.Contains("ngb-value:", values.Bind("10/20/2026 10:00:00 UTC"));
    }
}
