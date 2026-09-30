using FluentAssertions;
using NGB.Contracts.Common;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Common;

public sealed class InputTextLimitsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeSearch_EmptyValues_ReturnNull(string? value)
        => InputTextLimits.NormalizeSearch(value).Should().BeNull();

    [Fact]
    public void NormalizeSearch_TrimsBoundedValue()
        => InputTextLimits.NormalizeSearch("  invoice  ").Should().Be("invoice");

    [Fact]
    public void NormalizeSearch_RejectsOversizedValueBeforeTrimming()
    {
        var action = () => InputTextLimits.NormalizeSearch(
            new string('x', InputTextLimits.MaxSearchLength + 1),
            "query");

        action.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.Context["paramName"].Should().Be("query");
    }
}
