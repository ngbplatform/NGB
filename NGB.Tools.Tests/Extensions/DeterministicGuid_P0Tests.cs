using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Tools.Tests.Extensions;

public sealed class DeterministicGuid_P0Tests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WhenInputIsMissing_ThrowsRequired(string? input)
    {
        Action act = () => _ = DeterministicGuid.Create(input!);

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("stableInput");
    }

    [Fact]
    public void Create_IsDeterministic_ForSameInput()
    {
        var a1 = DeterministicGuid.Create("Dimension|buildings");
        var a2 = DeterministicGuid.Create("Dimension|buildings");

        a1.Should().Be(a2);
    }

    [Fact]
    public void Create_Changes_WhenInputChanges()
    {
        var a = DeterministicGuid.Create("Dimension|buildings");
        var b = DeterministicGuid.Create("Dimension|buildings2");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Create_SetsVariantAndVersionBits_InGuidByteArray()
    {
        var g = DeterministicGuid.Create("it.any");
        var bytes = g.ToByteArray();

        // Version bits are set in byte[7] (high nibble == 0x5).
        ((bytes[7] & 0xF0) >> 4).Should().Be(0x5);

        // Variant bits are set in byte[8] (top two bits == 10).
        ((bytes[8] & 0xC0) >> 6).Should().Be(0x2);
    }
}
