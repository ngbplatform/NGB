using FluentAssertions;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class SpecializedReportCursorCodecTests
{
    [Fact]
    public void Cursor_round_trips_and_rejects_wrong_report_or_malformed_payload()
    {
        var payload = new CursorPayload(25, 123, 45.6m);

        var encoded = SpecializedReportCursorCodec.Encode("report.a", payload);

        SpecializedReportCursorCodec.Decode<CursorPayload>("report.a", encoded)
            .Should().Be(payload);
        Action wrongReport = () => SpecializedReportCursorCodec.Decode<CursorPayload>("report.b", encoded);
        Action malformed = () => SpecializedReportCursorCodec.Decode<CursorPayload>("report.a", "not-base64");
        Action blankKind = () => SpecializedReportCursorCodec.Encode(" ", payload);
        Action blankCursor = () => SpecializedReportCursorCodec.Decode<CursorPayload>("report.a", " ");
        wrongReport.Should().Throw<NgbArgumentInvalidException>();
        malformed.Should().Throw<NgbArgumentInvalidException>();
        blankKind.Should().Throw<NgbArgumentRequiredException>();
        blankCursor.Should().Throw<NgbArgumentRequiredException>();

        var boundKind = SpecializedReportCursorCodec.BuildKind("report.a", "filter=1");
        var otherBoundKind = SpecializedReportCursorCodec.BuildKind("report.a", "filter=2");
        boundKind.Should().NotBe(otherBoundKind);
        Action wrongParameters = () => SpecializedReportCursorCodec.Decode<CursorPayload>(
            otherBoundKind,
            SpecializedReportCursorCodec.Encode(boundKind, payload));
        wrongParameters.Should().Throw<NgbArgumentInvalidException>();
    }

    private sealed record CursorPayload(int Offset, int Total, decimal Summary);
}
