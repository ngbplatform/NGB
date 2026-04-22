using FluentAssertions;
using NGB.Tools.Normalization;
using Xunit;

namespace NGB.Tools.Tests.Normalization;

public sealed class IdentifierNormalization_NormalizeStrictTableCode_P0Tests
{
    [Fact]
    public void NormalizeStrictTableCode_NormalizesAndLimits_WithHashSuffix_WhenRequired()
    {
        var input = "  Very-Long Table Code !!! with punctuation and spaces  ";

        var result = IdentifierNormalization.NormalizeStrictTableCode(
            code: input,
            paramName: "code",
            emptyResultMessage: "empty",
            maxTableCodeLen: 20);

        result.Length.Should().BeLessThanOrEqualTo(20);
        result.Should().MatchRegex("^[a-z0-9_]+$");

        // Because the input is long, we expect the hash suffix form.
        result.Should().MatchRegex("^.{7}_[0-9a-f]{12}$");
    }
}
