using System.Text;
using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Tools.Paging;
using Xunit;

namespace NGB.Tools.Tests.Paging;

public sealed class OpaqueCursorCodecFullCoverageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void BuildKind_and_Encode_reject_blank_cursor_kind(string? cursorKind)
    {
        ((Action)(() => OpaqueCursorCodec.BuildKind(cursorKind!)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => OpaqueCursorCodec.Encode(cursorKind!, new CursorPayload(1, "one"))))
            .Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void BuildKind_is_deterministic_query_bound_and_accepts_null_component_array()
    {
        var first = OpaqueCursorCodec.BuildKind("items", "active", null, "name:asc");
        var repeated = OpaqueCursorCodec.BuildKind("items", "active", null, "name:asc");
        var changed = OpaqueCursorCodec.BuildKind("items", "inactive", null, "name:asc");
        var noComponents = OpaqueCursorCodec.BuildKind("items", (string?[]?)null!);

        first.Should().Be(repeated).And.StartWith("items:");
        changed.Should().NotBe(first);
        noComponents.Should().Be(OpaqueCursorCodec.BuildKind("items"));
    }

    [Fact]
    public void Encode_and_decode_round_trip_typed_payload_with_url_safe_transport()
    {
        var payload = new CursorPayload(int.MaxValue, "value/with+symbols");

        var token = OpaqueCursorCodec.Encode("items:query", payload);
        var decoded = OpaqueCursorCodec.Decode<CursorPayload>("items:query", token);

        token.Should().NotContain("+").And.NotContain("/").And.NotEndWith("=");
        decoded.Should().Be(payload);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Decode_rejects_blank_cursor_kind(string? cursorKind)
    {
        var token = OpaqueCursorCodec.Encode("items", new CursorPayload(1, "one"));

        var act = () => OpaqueCursorCodec.Decode<CursorPayload>(cursorKind!, token);

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Decode_rejects_blank_cursor(string? cursor)
    {
        var act = () => OpaqueCursorCodec.Decode<CursorPayload>("items", cursor!);

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Decode_rejects_cursor_from_another_query_without_losing_domain_error()
    {
        var token = OpaqueCursorCodec.Encode("items:first-query", new CursorPayload(1, "one"));

        var act = () => OpaqueCursorCodec.Decode<CursorPayload>("items:second-query", token);

        act.Should().Throw<NgbArgumentInvalidException>()
            .Where(error => error.ParamName == "cursor");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("%%%")]
    public void Decode_normalizes_malformed_base64_to_invalid_cursor(string token)
    {
        var act = () => OpaqueCursorCodec.Decode<CursorPayload>("items", token);

        act.Should().Throw<NgbArgumentInvalidException>()
            .Where(error => error.ParamName == "cursor");
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{")]
    public void Decode_normalizes_malformed_json_to_invalid_cursor(string json)
    {
        var act = () => OpaqueCursorCodec.Decode<CursorPayload>("items", EncodeRaw(json));

        act.Should().Throw<NgbArgumentInvalidException>()
            .Where(error => error.ParamName == "cursor");
    }

    [Theory]
    [InlineData("{\"Version\":2,\"CursorKind\":\"items\",\"Payload\":{\"Offset\":1,\"Value\":\"one\"}}")]
    [InlineData("{\"Version\":1,\"CursorKind\":\"other\",\"Payload\":{\"Offset\":1,\"Value\":\"one\"}}")]
    [InlineData("{\"Version\":1,\"CursorKind\":\"items\",\"Payload\":null}")]
    [InlineData("null")]
    public void Decode_rejects_invalid_envelope_contract(string json)
    {
        var act = () => OpaqueCursorCodec.Decode<CursorPayload>("items", EncodeRaw(json));

        act.Should().Throw<NgbArgumentInvalidException>()
            .Where(error => error.ParamName == "cursor");
    }

    private static string EncodeRaw(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record CursorPayload(int Offset, string Value);
}
