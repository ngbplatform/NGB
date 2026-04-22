using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Tools.Tests.Extensions;

public sealed class GuidExtensions_EnsureRequired_And_NonEmpty_P0Tests
{
    [Fact]
    public void EnsureRequired_WhenNameIsBlank_ThrowsRequired()
    {
        Action act = () => Guid.NewGuid().EnsureRequired(" ");

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("name");
    }

    [Fact]
    public void EnsureRequired_WhenGuidEmpty_ThrowsRequired_ForProvidedName()
    {
        Action act = () => Guid.Empty.EnsureRequired("documentId");

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("documentId");
    }

    [Fact]
    public void EnsureRequired_WhenGuidNonEmpty_DoesNotThrow()
    {
        Action act = () => Guid.NewGuid().EnsureRequired("documentId");

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNonEmpty_WhenGuidEmpty_ThrowsOutOfRange()
    {
        Action act = () => Guid.Empty.EnsureNonEmpty("id");

        var ex = act.Should().Throw<NgbArgumentOutOfRangeException>().Which;
        ex.ParamName.Should().Be("id");
        ex.ActualValue.Should().Be(Guid.Empty);
    }

    [Fact]
    public void EnsureNonEmpty_WhenGuidNonEmpty_DoesNotThrow()
    {
        Action act = () => Guid.NewGuid().EnsureNonEmpty("id");

        act.Should().NotThrow();
    }
}
