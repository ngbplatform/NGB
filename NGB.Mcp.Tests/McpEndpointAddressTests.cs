using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NGB.Hosting.AspNetCore;
using NGB.Mcp.AspNetCore;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpEndpointAddressTests
{
    [Theory]
    [InlineData("https://api.example", "/mcp", "https://api.example/mcp")]
    [InlineData("https://api.example/", "/mcp", "https://api.example/mcp")]
    [InlineData("https://api.example/ngb/", "/mcp", "https://api.example/ngb/mcp")]
    [InlineData("https://api.example", "/integration/mcp", "https://api.example/integration/mcp")]
    [InlineData("http://pm-api.localhost:5051", "/mcp", "http://pm-api.localhost:5051/mcp")]
    public void PublicApiBaseAndMappedRouteDetermineTheCanonicalAddress(string baseUrl, string route, string expected)
    {
        var services = new ServiceCollection();
        var settings = new Dictionary<string, string?>
        {
            ["Ngb:Hosting:PublicApiUrl"] = baseUrl,
            ["FeatureManagement:Mcp"] = "true"
        };
        if (baseUrl.StartsWith("http:", StringComparison.Ordinal))
            settings["Mcp:AllowLocalhostHttp"] = "true";

        services.AddNgbMcp(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using var provider = services.BuildServiceProvider();
        var address = provider.GetRequiredService<McpEndpointAddress>();
        Assert.Throws<InvalidOperationException>(() => address.Resource);

        address.Map(route);

        Assert.Equal(expected, address.Resource.AbsoluteUri);
        Assert.Equal(new Uri(expected).GetLeftPart(UriPartial.Authority)
            + "/.well-known/oauth-protected-resource" + new Uri(expected).AbsolutePath, address.MetadataUri.AbsoluteUri);
        Assert.Throws<InvalidOperationException>(() => address.Map(route));
    }

    [Fact]
    public void ExplicitResourceOverridesTheCommonApiAddress()
    {
        var address = new McpEndpointAddress(new NgbMcpOptions { Resource = "https://mcp.example/read" },
            new NgbHostingOptions { PublicApiUrl = "https://api.example" });

        address.Map("/read");

        Assert.Equal("https://mcp.example/read", address.Resource.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://api.example")]
    [InlineData("https://user@api.example")]
    [InlineData("https://api.example?query=1")]
    [InlineData("https://api.example#fragment")]
    [InlineData("not-a-url")]
    public void MissingOrUnsafePublicAddressFailsRegistration(string publicUrl)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new McpEndpointAddress(new NgbMcpOptions(), new NgbHostingOptions { PublicApiUrl = publicUrl }));

        Assert.Contains("Ngb:Hosting:PublicApiUrl", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("mcp")]
    [InlineData("//evil.example")]
    [InlineData("/{tenant}/mcp")]
    [InlineData("/../mcp")]
    [InlineData("/mcp?query=1")]
    [InlineData("/mcp#fragment")]
    [InlineData("/mcp%2fadmin")]
    [InlineData("/mcp\\admin")]
    [InlineData("/mcp admin")]
    public void NonLiteralOrAmbiguousRoutesFailMapping(string route)
    {
        var address = new McpEndpointAddress(new NgbMcpOptions(), new NgbHostingOptions { PublicApiUrl = "https://api.example" });

        Assert.Throws<ArgumentException>(() => address.Map(route));
    }
}
