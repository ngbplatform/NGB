using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;
using Xunit;

namespace NGB.Tools.Tests.Normalization;

public sealed class IdentifierNormalization_NormalizeStrictToken_P0Tests
{
    [Fact]
    public void NormalizeStrictToken_WhenNull_ThrowsRequired()
    {
        Action act = () => _ = IdentifierNormalization.NormalizeStrictToken(null, "code", "empty");

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("code");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeStrictToken_WhenBlank_ThrowsInvalid(string input)
    {
        Action act = () => _ = IdentifierNormalization.NormalizeStrictToken(input, "code", "empty");

        act.Should().Throw<NgbArgumentInvalidException>()
            .Which.Message.Should().Be("Code must be non-empty.");
    }

    [Theory]
    [InlineData("A--B", "a_b")]
    [InlineData("__A__", "a")]
    [InlineData("a...b", "a_b")]
    [InlineData("  A  B  ", "a_b")]
    [InlineData("A#B$C", "a_b_c")]
    public void NormalizeStrictToken_NormalizesAsciiAndPunctuation(string input, string expected)
    {
        var result = IdentifierNormalization.NormalizeStrictToken(input, "code", "empty");

        result.Should().Be(expected);
        result.Should().MatchRegex("^[a-z0-9_]+$");
    }

    [Fact]
    public void NormalizeStrictToken_WhenResultIsEmpty_ThrowsInvalid_WithEmptyResultMessage()
    {
        Action act = () => _ = IdentifierNormalization.NormalizeStrictToken("!!!", "code", "token became empty");

        act.Should().Throw<NgbArgumentInvalidException>()
            .Which.Message.Should().Be("token became empty");
    }
}
