using System.Text.Json;
using FluentAssertions;
using NGB.Contracts.Common;
using NGB.Runtime.Validation;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Validation;

public sealed class RecordPayloadLimitsTests
{
    [Fact]
    public void EnsureWithinLimits_AcceptsExactBoundaries()
    {
        var row = Cells(RecordPayloadLimits.MaxCells / RecordPayloadLimits.MaxRows);
        var rows = Enumerable.Repeat<IReadOnlyDictionary<string, JsonElement>>(
            row,
            RecordPayloadLimits.MaxRows).ToArray();

        var action = () => RecordPayloadLimits.EnsureWithinLimits(new Dictionary<string, RecordPartPayload> { ["lines"] = new(rows) });

        action.Should().NotThrow();
    }

    [Fact]
    public void EnsureWithinLimits_RejectsTooManyPartsRowsAndCells()
    {
        var tooManyParts = Enumerable.Range(0, RecordPayloadLimits.MaxParts + 1)
            .ToDictionary(
                index => $"part-{index}",
                _ => new RecordPartPayload([]));
        var empty = new Dictionary<string, JsonElement>();
        var tooManyRows = Enumerable.Repeat<IReadOnlyDictionary<string, JsonElement>>(
            empty,
            RecordPayloadLimits.MaxRows + 1).ToArray();
        var tooManyCells = Enumerable.Repeat<IReadOnlyDictionary<string, JsonElement>>(
            Cells(RecordPayloadLimits.MaxCells / RecordPayloadLimits.MaxRows + 1),
            RecordPayloadLimits.MaxRows).ToArray();

        ((Action)(() => RecordPayloadLimits.EnsureWithinLimits(tooManyParts)))
            .Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("payload.parts");
        ((Action)(() => RecordPayloadLimits.EnsureWithinLimits(
                new Dictionary<string, RecordPartPayload> { ["lines"] = new(tooManyRows) })))
            .Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("payload.partRows");
        ((Action)(() => RecordPayloadLimits.EnsureWithinLimits(
                new Dictionary<string, RecordPartPayload> { ["lines"] = new(tooManyCells) })))
            .Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("payload.partCells");
    }

    private static IReadOnlyDictionary<string, JsonElement> Cells(int count)
        => Enumerable.Range(0, count).ToDictionary(
            index => $"field-{index}",
            _ => JsonSerializer.SerializeToElement(1));
}
