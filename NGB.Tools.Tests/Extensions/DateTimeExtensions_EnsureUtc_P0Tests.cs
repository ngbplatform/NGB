using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Tools.Tests.Extensions;

public sealed class DateTimeExtensions_EnsureUtc_P0Tests
{
    [Fact]
    public void EnsureUtc_WhenNameIsBlank_ThrowsRequired()
    {
        var dt = DateTime.UtcNow;

        Action act = () => dt.EnsureUtc(" ");

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("name");
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void EnsureUtc_WhenDateTimeIsNotUtc_ThrowsInvalid(DateTimeKind kind)
    {
        var dt = new DateTime(2026, 2, 19, 1, 2, 3, kind);

        Action act = () => dt.EnsureUtc("fromUtc");

        act.Should().Throw<NgbArgumentInvalidException>()
            .Which.ParamName.Should().Be("fromUtc");
    }

    [Fact]
    public void EnsureUtc_WhenDateTimeIsUtc_DoesNotThrow()
    {
        var dt = new DateTime(2026, 2, 19, 1, 2, 3, DateTimeKind.Utc);

        Action act = () => dt.EnsureUtc("fromUtc");

        act.Should().NotThrow();
    }
}
