using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Watchdog.Hosting;
using Xunit;

namespace NGB.Trade.Watchdog.IntegrationTests;

public sealed class ProgramTests
{
    [Fact]
    public void Program_StartsWithExpectedBranding()
    {
        using var environment = new EnvironmentVariableScope(
            "Serilog__WriteTo__1__Args__serverUrl",
            "http://localhost:5341");
        using var factory = new WebApplicationFactory<Program>();

        factory.Services.GetRequiredService<IOptions<WatchdogOptions>>().Value.PageTitle
            .Should().Be("NGB: Trade - Health");
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}
