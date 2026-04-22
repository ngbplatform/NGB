using FluentAssertions;
using NGB.Tools.Normalization;
using Xunit;

namespace NGB.Tools.Tests.Normalization;

public sealed class IdentifierNormalization_NormalizeStrictColumnCode_P0Tests
{
    [Fact]
    public void NormalizeStrictColumnCode_WhenStartsWithDigit_PrependsDigitPrefix()
    {
        var result = IdentifierNormalization.NormalizeStrictColumnCode(
            code: "1abc",
            paramName: "code",
            emptyResultMessage: "empty",
            maxSqlIdentifierLen: 63,
            digitPrefix: "c_");

        result.Should().Be("c_1abc");
        result[0].Should().NotBeInRange('0', '9');
    }

    [Fact]
    public void NormalizeStrictColumnCode_WhenLeadingNoiseCollapsesToDigit_PrependsDigitPrefix()
    {
        var result = IdentifierNormalization.NormalizeStrictColumnCode(
            code: "***1",
            paramName: "code",
            emptyResultMessage: "empty",
            maxSqlIdentifierLen: 63,
            digitPrefix: "c_");

        result.Should().Be("c_1");
    }

    [Fact]
    public void NormalizeStrictColumnCode_WhenStartsWithLetter_DoesNotPrependDigitPrefix()
    {
        var result = IdentifierNormalization.NormalizeStrictColumnCode(
            code: "Abc",
            paramName: "code",
            emptyResultMessage: "empty",
            maxSqlIdentifierLen: 63,
            digitPrefix: "c_");

        result.Should().Be("abc");
    }

    [Fact]
    public void NormalizeStrictColumnCode_StillLimits_WithHashSuffix_WhenLong()
    {
        var input = "1" + new string('a', 200);

        var result = IdentifierNormalization.NormalizeStrictColumnCode(
            code: input,
            paramName: "code",
            emptyResultMessage: "empty",
            maxSqlIdentifierLen: 20,
            digitPrefix: "c_");

        result.Length.Should().BeLessThanOrEqualTo(20);
        result.Should().MatchRegex("^[a-z0-9_]+$");
        result.Should().MatchRegex("^.{7}_[0-9a-f]{12}$");
        result[0].Should().NotBeInRange('0', '9');
    }

    [Fact]
    public void NormalizeStrictColumnCode_WhenDigitPrefixIsEmpty_DoesNotThrow_AsLongAsTokenIsNotEmpty()
    {
        // This test documents current behavior: digitPrefix is a caller contract.
        // If a caller passes an empty prefix, the resulting token may start with a digit.
        var result = IdentifierNormalization.NormalizeStrictColumnCode(
            code: "1",
            paramName: "code",
            emptyResultMessage: "empty",
            maxSqlIdentifierLen: 63,
            digitPrefix: string.Empty);

        result.Should().Be("1");
    }
}
