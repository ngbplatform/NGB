using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpLinkTests
{
    [Theory]
    [InlineData("http://ngb.example")]
    [InlineData("ftp://localhost")]
    [InlineData("relative/path")]
    public void DevelopmentOverrideStillRejectsNonLocalOrNonHttpOrigins(string origin)
    {
        Assert.Throws<ArgumentException>(() => new ConfiguredMcpNgbLinkResolver(new Uri(origin, UriKind.RelativeOrAbsolute),
            new Dictionary<string, string>(), allowLocalhostHttp: true));
    }

    [Fact]
    public void OnlyConfiguredCanonicalSourcesReceiveLinks()
    {
        var resolver = new ConfiguredMcpNgbLinkResolver(new Uri("https://ngb.example"), new Dictionary<string, string>
        {
            ["document"] = "/documents/{typeCode}/{id}",
            ["report"] = "/reports/{typeCode}"
        });
        var id = Guid.NewGuid();
        Assert.Equal($"https://ngb.example/documents/custom.invoice/{id:D}", resolver.Resolve("document", "custom.invoice", id)!.AbsoluteUri);
        Assert.Equal("https://ngb.example/reports/custom.balance", resolver.Resolve("report", "custom.balance", null)!.AbsoluteUri);
        Assert.Null(resolver.Resolve("catalog", "custom.customer", id));
        Assert.Null(resolver.Resolve("document", "custom.invoice", null));
        Assert.Null(resolver.Resolve("document", "custom.invoice", Guid.Empty));
        Assert.Null(resolver.Resolve("report", "../secret", null));
        Assert.Null(resolver.Resolve("report", "https://evil.example", null));
        Assert.Null(resolver.Resolve("report", string.Empty, null));
        Assert.Null(resolver.Resolve("report", new string('a', 129), null));
    }

    [Theory]
    [InlineData("http://ngb.example", "/reports/{typeCode}")]
    [InlineData("https://user@ngb.example", "/reports/{typeCode}")]
    [InlineData("https://ngb.example/path", "/reports/{typeCode}")]
    [InlineData("https://ngb.example?query=1", "/reports/{typeCode}")]
    [InlineData("https://ngb.example#fragment", "/reports/{typeCode}")]
    [InlineData("https://ngb.example", "//evil.example/{typeCode}")]
    [InlineData("https://ngb.example", "/../{typeCode}")]
    [InlineData("https://ngb.example", "/{unknown}")]
    [InlineData("https://ngb.example", "/{id}")]
    [InlineData("https://ngb.example", "/reports?redirect={typeCode}")]
    [InlineData("https://ngb.example", "")]
    public void UnsafeConfigurationFailsAtStartup(string origin, string route)
    {
        Assert.Throws<ArgumentException>(() => new ConfiguredMcpNgbLinkResolver(new Uri(origin),
            new Dictionary<string, string> { ["report"] = route }));
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://pm-ui.localhost:5173")]
    public void LocalDevelopmentRequiresExplicitOptIn(string origin)
    {
        var routes = new Dictionary<string, string> { ["report"] = "/reports/{typeCode}" };
        Assert.Throws<ArgumentException>(() => new ConfiguredMcpNgbLinkResolver(new Uri(origin), routes));
        var resolver = new ConfiguredMcpNgbLinkResolver(new Uri(origin), routes, allowLocalhostHttp: true);
        Assert.Equal(origin + "/reports/custom.test", resolver.Resolve("report", "custom.test", null)!.AbsoluteUri);
    }

    [Fact]
    public void UnsupportedKindsAndRecordRoutesWithoutIdentityFailRegistration()
    {
        var origin = new Uri("https://ngb.example");
        Assert.Throws<ArgumentException>(() => new ConfiguredMcpNgbLinkResolver(origin,
            new Dictionary<string, string> { ["register"] = "/registers/{typeCode}/{id}" }));
        Assert.Throws<ArgumentException>(() => new ConfiguredMcpNgbLinkResolver(origin,
            new Dictionary<string, string> { ["catalog"] = "/catalogs/{typeCode}" }));
        var resolver = new ConfiguredMcpNgbLinkResolver(origin,
            new Dictionary<string, string> { ["report"] = "/reports/{typeCode}" });
        Assert.Equal("https://ngb.example/reports/test", resolver.Resolve("report", "test", Guid.NewGuid())!.AbsoluteUri);
    }
}
