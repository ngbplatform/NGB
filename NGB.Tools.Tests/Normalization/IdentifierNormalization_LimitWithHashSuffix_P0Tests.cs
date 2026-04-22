using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;
using Xunit;

namespace NGB.Tools.Tests.Normalization;

public sealed class IdentifierNormalization_LimitWithHashSuffix_P0Tests
{
    [Fact]
    public void LimitWithHashSuffix_WhenTokenFits_ReturnsSameToken()
    {
        var result = IdentifierNormalization.LimitWithHashSuffix("abc", maxLen: 10);

        result.Should().Be("abc");
    }

    [Fact]
    public void LimitWithHashSuffix_WhenTokenTooLong_TruncatesAndAppendsDeterministicSuffix()
    {
        var token = "abcdefghijklmnopqrstuvwxyz";

        var result1 = IdentifierNormalization.LimitWithHashSuffix(token, maxLen: 20);
        var result2 = IdentifierNormalization.LimitWithHashSuffix(token, maxLen: 20);

        result1.Should().Be(result2);
        result1.Length.Should().Be(20);
        result1.Should().MatchRegex("^[a-z0-9_]+$");
        result1.Should().MatchRegex("^.{7}_[0-9a-f]{12}$");
    }

    [Fact]
    public void LimitWithHashSuffix_WhenMaxLenIsTooSmall_ThrowsInvariantViolation_WithContext()
    {
        var maxLen = IdentifierNormalization.HashSuffixLen;
        var token = new string('a', maxLen + 1);

        Action act = () => _ = IdentifierNormalization.LimitWithHashSuffix(token, maxLen);

        var ex = act.Should().Throw<NgbInvariantViolationException>().Which;
        ex.Context.Should().ContainKey("maxLen").WhoseValue.Should().Be(maxLen);
        ex.Context.Should().ContainKey("hashSuffixLen").WhoseValue.Should().Be(IdentifierNormalization.HashSuffixLen);
    }
}
