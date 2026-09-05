using FluentAssertions;
using NGB.OperationalRegisters.Contracts;
using NGB.Tools.Exceptions;
using NGB.Trade.Runtime.Reporting;

namespace NGB.Trade.Runtime.Tests.Reporting;

public sealed class InventoryMovementCursorCodecFullCoverageTests
{
    [Fact]
    public void Cursor_round_trips_and_rejects_each_malformed_component()
    {
        var cursor = new OperationalRegisterOccurredAtCursor(
            new DateTime(2026, 8, 23, 12, 34, 56, DateTimeKind.Utc),
            42);

        InventoryMovementCursorCodec.Decode(InventoryMovementCursorCodec.Encode(cursor))
            .Should().Be(cursor);

        foreach (var invalid in new[]
                 {
                     "",
                     "timestamp-only",
                     "not-a-date|42",
                     "2026-08-23T12:34:56Z|not-an-id",
                     "2026-08-23T12:34:56Z|0",
                     "2026-08-23T12:34:56Z|-1"
                 })
        {
            ((Action)(() => InventoryMovementCursorCodec.Decode(invalid)))
                .Should().Throw<NgbArgumentInvalidException>();
        }
    }
}
