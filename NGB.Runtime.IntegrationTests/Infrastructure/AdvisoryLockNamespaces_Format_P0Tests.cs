using FluentAssertions;
using NGB.PostgreSql.Locks;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class AdvisoryLockNamespaces_Format_P0Tests
{
    [Fact]
    public void Format_RendersExpectedTags()
    {
        AdvisoryLockNamespaces.Format(AdvisoryLockNamespaces.Document).Should().Be("DOC\\x01");
        AdvisoryLockNamespaces.Format(AdvisoryLockNamespaces.Catalog).Should().Be("CAT\\x01");
        AdvisoryLockNamespaces.Format(AdvisoryLockNamespaces.Period).Should().Be("PER\\x01");
    }

    [Fact]
    public void Format_MatchesPackOutput()
    {
        var ns = AdvisoryLockNamespaces.Pack("PER", 1);
        AdvisoryLockNamespaces.Format(ns).Should().Be("PER\\x01");
    }
}
