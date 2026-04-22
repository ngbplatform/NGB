using System.Globalization;
using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;
using Xunit;

namespace NGB.Tools.Tests.Normalization;

public sealed class CodeNormalizer_NormalizeCodeNorm_P0Tests
{
    [Fact]
    public void NormalizeCodeNorm_WhenNull_ThrowsRequired_WithProvidedParamName()
    {
        Action act = () => _ = CodeNormalizer.NormalizeCodeNorm(null, "code");

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("code");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n\r")]
    public void NormalizeCodeNorm_WhenBlank_ThrowsInvalid(string code)
    {
        Action act = () => _ = CodeNormalizer.NormalizeCodeNorm(code, "code");

        act.Should().Throw<NgbArgumentInvalidException>()
            .Which.ParamName.Should().Be("code");
    }

    [Theory]
    [InlineData(" AbC ", "abc")]
    [InlineData("Hello-World", "hello-world")]
    [InlineData("  A.B,C;D  ", "a.b,c;d")]
    [InlineData("\tAbC\n", "abc")]
    [InlineData("  A  B  ", "a  b")] // internal whitespace is preserved; only trimming happens
    [InlineData("  A_1  ", "a_1")]
    public void NormalizeCodeNorm_TrimsAndLowercasesInvariant(string input, string expected)
    {
        var result = CodeNormalizer.NormalizeCodeNorm(input, "code");

        result.Should().Be(expected);
    }

    [Fact]
    public void NormalizeCodeNorm_IsIdempotent()
    {
        var once = CodeNormalizer.NormalizeCodeNorm("  AbC-123  ", "code");
        var twice = CodeNormalizer.NormalizeCodeNorm(once, "code");

        twice.Should().Be(once);
    }

    [Fact]
    public void NormalizeCodeNorm_UsesInvariantLowercasing_EvenIfCurrentCultureIsTurkish()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");

            // Culture-sensitive ToLower("I") under tr-TR would yield dotless i, but invariant should yield "i".
            var result = CodeNormalizer.NormalizeCodeNorm("I", "code");

            result.Should().Be("i");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
