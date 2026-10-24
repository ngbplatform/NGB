using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Features;
using NGB.Mcp.AspNetCore;
using NGB.Runtime.Features;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpTransportConfigurationTests
{
    [Theory]
    [InlineData("Mcp:Resource", "http://ngb.example/mcp")]
    [InlineData("Mcp:Resource", "https://ngb.example/")]
    [InlineData("Mcp:Resource", "not-a-uri")]
    [InlineData("Mcp:Resource", "https://user@ngb.example/mcp")]
    [InlineData("Mcp:Resource", "https://ngb.example/mcp?query=1")]
    [InlineData("Mcp:Resource", "https://ngb.example/mcp#fragment")]
    [InlineData("Mcp:AuthenticationScheme", "")]
    [InlineData("Mcp:AuthenticationScheme", "NgbMcp")]
    [InlineData("Mcp:ConcurrentRequests", "0")]
    [InlineData("Mcp:ConcurrentRequests", "257")]
    [InlineData("Mcp:RequestsPerMinute", "0")]
    [InlineData("Mcp:RequestsPerMinute", "100001")]
    [InlineData("Mcp:RequestTimeoutSeconds", "0")]
    [InlineData("Mcp:RequestTimeoutSeconds", "301")]
    [InlineData("Mcp:MaxRequestBytes", "0")]
    [InlineData("Mcp:MaxRequestBytes", "-1")]
    [InlineData("Mcp:AllowedOrigins:0", "https://client.example/path")]
    [InlineData("Mcp:AllowedOrigins:0", "http://client.example")]
    public void UnsafeOrUnboundedTransportConfigurationFailsBeforeRegistration(string name, string value)
    {
        var values = new Dictionary<string, string?>
        {
            ["FeatureManagement:Mcp"] = "true",
            ["Mcp:Resource"] = "https://ngb.example/mcp",
            [name] = value
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNgbMcp(configuration));
    }

    [Fact]
    public void AbsentConfigurationKeepsMcpDisabled()
    {
        var services = new ServiceCollection();
        services.AddNgbMcp(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<NgbFeatureRegistry>().Contains(NgbFeatures.Mcp));
        Assert.Null(provider.GetService<McpEndpointAddress>());
        Assert.Null(provider.GetService<McpReadToolRegistry>());
    }

    [Fact]
    public void ObsoleteAudienceSettingFailsWithMigrationGuidance()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Audience"] = "legacy-mcp-audience"
        }).Build();

        var error = Assert.Throws<NgbConfigurationViolationException>(() => new ServiceCollection().AddNgbMcp(configuration));

        Assert.Contains("Remove Mcp:Audience", error.Message);
    }

    [Theory]
    [InlineData("Mcp:Enabled", "true")]
    [InlineData("Mcp:Enabled", "false")]
    [InlineData("FeatureManagement:Mcp", "yes")]
    [InlineData("FeatureManagement:mcp", "true")]
    [InlineData("FeatureManagement:Mcp:Enabled", "true")]
    public void ObsoleteOrInvalidSwitchFailsWithTheRegisteredFeatureKey(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = value
        }).Build();

        var error = Assert.Throws<NgbConfigurationViolationException>(() => new ServiceCollection().AddNgbMcp(configuration));

        Assert.Contains("FeatureManagement:", error.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_048_577)]
    [InlineData(int.MaxValue)]
    public void BodyBudgetAcceptsPositiveDeploymentLimits(int maximumBytes)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:Mcp"] = "true",
            ["Mcp:Resource"] = "https://ngb.example/mcp",
            ["Mcp:MaxRequestBytes"] = maximumBytes.ToString()
        }).Build();
        var services = new ServiceCollection();
        services.AddNgbMcp(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(maximumBytes, provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<NgbMcpOptions>>().Value.MaxRequestBytes);
    }

    [Theory]
    [InlineData("http://remote.example/mcp")]
    [InlineData("ftp://localhost/mcp")]
    public void LocalDevelopmentFlagDoesNotAllowRemoteHttpOrOtherSchemes(string resource)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:Mcp"] = "true",
            ["Mcp:Resource"] = resource,
            ["Mcp:AllowLocalhostHttp"] = "true"
        }).Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNgbMcp(configuration));
    }
}
