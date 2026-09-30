using System.Text;
using FluentAssertions;
using NGB.Runtime.Common;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Common;

public sealed class ListPageCursorCodecFullCoverageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decode_RejectsMissingCursor(string? value)
    {
        var action = () => ListPageCursorCodec.Decode(value!);

        action.Should().Throw<NgbArgumentInvalidException>()
            .Which.Should().Match<NgbArgumentInvalidException>(exception =>
                exception.ParamName == "cursor" &&
                exception.Reason == "Cursor must not be empty.");
    }

    [Fact]
    public void Encode_RejectsEmptyId()
    {
        var action = () => ListPageCursorCodec.Encode("display", Guid.Empty);

        action.Should().Throw<NgbArgumentInvalidException>()
            .Which.Should().Match<NgbArgumentInvalidException>(exception =>
                exception.ParamName == "afterId" &&
                exception.Reason == "Cursor ID must not be empty.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" Invoice / 42 ")]
    public void EncodeAndDecode_RoundTripsUrlSafePayload(string? display)
    {
        var id = Guid.Parse("90f5df60-0f49-46fe-a742-ab995f8940f8");

        var encoded = ListPageCursorCodec.Encode(display, id);
        var decoded = ListPageCursorCodec.Decode($"  {encoded}  ");

        encoded.Should().NotContainAny("+", "/", "=");
        decoded.Should().Be(new ListPageCursor(display, id));
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void Decode_RejectsMalformedUnsupportedAndIncompletePayload(string value)
    {
        var action = () => ListPageCursorCodec.Decode(value);

        action.Should().Throw<NgbArgumentInvalidException>()
            .Which.Should().Match<NgbArgumentInvalidException>(exception =>
                exception.ParamName == "cursor" &&
                exception.Reason == "Cursor is invalid.");
    }

    public static TheoryData<string> InvalidPayloads => new()
    {
        "not-base64!",
        EncodeRaw("{"),
        EncodeRaw("null"),
        EncodeRaw("{\"version\":2,\"afterDisplay\":\"display\",\"afterId\":\"90f5df60-0f49-46fe-a742-ab995f8940f8\"}"),
        EncodeRaw("{\"version\":1,\"afterDisplay\":\"display\",\"afterId\":\"00000000-0000-0000-0000-000000000000\"}")
    };

    private static string EncodeRaw(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
