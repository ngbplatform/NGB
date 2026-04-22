using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Tools.Tests.Extensions;

public sealed class DateOnlyExtensions_EnsureMonthStart_P0Tests
{
    [Fact]
    public void EnsureMonthStart_WhenNameIsBlank_ThrowsRequired()
    {
        var d = new DateOnly(2026, 2, 1);

        Action act = () => d.EnsureMonthStart(" ");

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("name");
    }

    [Fact]
    public void EnsureMonthStart_WhenNotFirstDay_ThrowsOutOfRange()
    {
        var d = new DateOnly(2026, 2, 2);

        Action act = () => d.EnsureMonthStart("period");

        act.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("period");
    }

    [Fact]
    public void EnsureMonthStart_WhenFirstDay_DoesNotThrow()
    {
        var d = new DateOnly(2026, 2, 1);

        Action act = () => d.EnsureMonthStart("period");

        act.Should().NotThrow();
    }
}
