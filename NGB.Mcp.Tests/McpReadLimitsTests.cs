using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpReadLimitsTests
{
    [Theory]
    [InlineData(nameof(McpReadOptions.MaxInputBytes), 0)]
    [InlineData(nameof(McpReadOptions.MaxInputBytes), -1)]
    [InlineData(nameof(McpReadOptions.MaxResponseBytes), 0)]
    [InlineData(nameof(McpReadOptions.MaxResponseBytes), -1)]
    [InlineData(nameof(McpReadOptions.MaxJsonDepth), 0)]
    [InlineData(nameof(McpReadOptions.MaxJsonDepth), -1)]
    [InlineData(nameof(McpReadOptions.MaxJsonDepth), McpReadOptions.MaximumSupportedJsonDepth + 1)]
    [InlineData(nameof(McpReadOptions.MaxRecordRows), 0)]
    [InlineData(nameof(McpReadOptions.MaxRecordRows), -1)]
    [InlineData(nameof(McpReadOptions.MaxRecordBytes), 0)]
    [InlineData(nameof(McpReadOptions.MaxRecordBytes), -1)]
    [InlineData(nameof(McpReadOptions.MaxTools), 0)]
    [InlineData(nameof(McpReadOptions.MaxTools), -1)]
    public void InvalidReadLimitsIdentifyTheSettingAtStartup(string setting, int value)
    {
        var services = new ServiceCollection();
        services.AddNgbMcpReadTools(options => Set(options, setting, value));
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains($"{nameof(McpReadOptions)}.{setting}", Assert.Single(error.Failures));
    }

    [Theory]
    [InlineData(nameof(McpReadOptions.MaxInputBytes), 1)]
    [InlineData(nameof(McpReadOptions.MaxInputBytes), 1_048_577)]
    [InlineData(nameof(McpReadOptions.MaxInputBytes), int.MaxValue)]
    [InlineData(nameof(McpReadOptions.MaxResponseBytes), 1)]
    [InlineData(nameof(McpReadOptions.MaxResponseBytes), 4_194_305)]
    [InlineData(nameof(McpReadOptions.MaxResponseBytes), int.MaxValue)]
    [InlineData(nameof(McpReadOptions.MaxRecordRows), 1)]
    [InlineData(nameof(McpReadOptions.MaxRecordRows), 10_001)]
    [InlineData(nameof(McpReadOptions.MaxRecordRows), int.MaxValue)]
    [InlineData(nameof(McpReadOptions.MaxRecordBytes), 1)]
    [InlineData(nameof(McpReadOptions.MaxRecordBytes), 4_194_305)]
    [InlineData(nameof(McpReadOptions.MaxRecordBytes), int.MaxValue)]
    [InlineData(nameof(McpReadOptions.MaxTools), 1)]
    [InlineData(nameof(McpReadOptions.MaxTools), 251)]
    [InlineData(nameof(McpReadOptions.MaxTools), int.MaxValue)]
    [InlineData(nameof(McpReadOptions.MaxJsonDepth), 1)]
    [InlineData(nameof(McpReadOptions.MaxJsonDepth), McpReadOptions.MaximumSupportedJsonDepth)]
    public void DeploymentBudgetsAreNotRestrictedByTheOldArbitraryRanges(string setting, int value)
    {
        var services = new ServiceCollection();
        services.AddNgbMcpReadTools(options => Set(options, setting, value));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptions<McpReadOptions>>().Value;
        Assert.Equal(value, typeof(McpReadOptions).GetProperty(setting)!.GetValue(options));
    }

    [Fact]
    public void ValidationReportsAllInvalidSettingsAndRegistrationIsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddNgbMcpReadTools(options =>
        {
            options.MaxInputBytes = 0;
            options.MaxResponseBytes = 0;
            options.MaxRecordRows = 0;
            options.MaxRecordBytes = 0;
            options.MaxTools = 0;
            options.MaxJsonDepth = 0;
        });
        services.AddNgbMcpReadTools();
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IValidateOptions<McpReadOptions>>());
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Equal(6, error.Failures.Count());
    }

    [Fact]
    public void NullOptionsAreRejectedByTheValidator()
    {
        Assert.Throws<ArgumentNullException>(() => new McpReadOptionsValidator().Validate(null, null!));
    }

    private static void Set(McpReadOptions options, string setting, int value)
        => typeof(McpReadOptions).GetProperty(setting)!.SetValue(options, value);
}
