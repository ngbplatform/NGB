using FluentAssertions;
using NGB.PostgreSql.Locks;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class AdvisoryLockNamespaces_Pack_P0Tests
{
    [Fact]
    public void Pack_ProducesExpectedNamespaceConstants()
    {
        AdvisoryLockNamespaces.Pack("DOC", 1).Should().Be(AdvisoryLockNamespaces.Document);
        AdvisoryLockNamespaces.Pack("CAT", 1).Should().Be(AdvisoryLockNamespaces.Catalog);
        AdvisoryLockNamespaces.Pack("PER", 1).Should().Be(AdvisoryLockNamespaces.Period);
    }

    [Fact]
    public void Pack_InvalidInputs_Throw()
    {
        FluentActions.Invoking(() => AdvisoryLockNamespaces.Pack(null!, 1))
            .Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("tag3");

        FluentActions.Invoking(() => AdvisoryLockNamespaces.Pack("", 1))
            .Should().Throw<NgbArgumentInvalidException>()
            .Which.ParamName.Should().Be("tag3");

        FluentActions.Invoking(() => AdvisoryLockNamespaces.Pack("AB", 1))
            .Should().Throw<NgbArgumentInvalidException>()
            .Which.ParamName.Should().Be("tag3");

        FluentActions.Invoking(() => AdvisoryLockNamespaces.Pack("ABCD", 1))
            .Should().Throw<NgbArgumentInvalidException>()
            .Which.ParamName.Should().Be("tag3");

        // non-ASCII
        FluentActions.Invoking(() => AdvisoryLockNamespaces.Pack("ДОК", 1))
            .Should().Throw<NgbArgumentInvalidException>()
            .Which.ParamName.Should().Be("tag3");
    }
}
