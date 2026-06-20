using FluentAssertions;
using NGB.Core.Security;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class NgbPermissionKeyTests
{
    [Fact]
    public void Constructor_NormalizesSegments_AndAllowsDottedResourceCode()
    {
        var key = new NgbPermissionKey(" Document ", " PM.Lease ", " View ");

        key.ResourceKind.Should().Be("document");
        key.ResourceCode.Should().Be("pm.lease");
        key.ActionCode.Should().Be("view");
        key.ToString().Should().Be("document.pm.lease.view");
    }

    [Theory]
    [InlineData("document.pm.lease.view", "document", "pm.lease", "view")]
    [InlineData("report.pm.ar.aging.execute", "report", "pm.ar.aging", "execute")]
    public void Parse_UsesFirstAndLastSegments_AsKindAndAction(string value, string kind, string resource, string action)
    {
        var key = NgbPermissionKey.Parse(value);

        key.ResourceKind.Should().Be(kind);
        key.ResourceCode.Should().Be(resource);
        key.ActionCode.Should().Be(action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("document.view")]
    public void Parse_RejectsMalformedKeys(string value)
    {
        var act = () => NgbPermissionKey.Parse(value);

        act.Should().Throw<NgbException>();
    }

    [Theory]
    [InlineData("doc.ument", "pm.lease", "view")]
    [InlineData("document", "pm.lease", "vi.ew")]
    public void Constructor_RejectsDotsInKindAndAction(string kind, string resource, string action)
    {
        var act = () => new NgbPermissionKey(kind, resource, action);

        act.Should().Throw<NgbArgumentInvalidException>();
    }
}
