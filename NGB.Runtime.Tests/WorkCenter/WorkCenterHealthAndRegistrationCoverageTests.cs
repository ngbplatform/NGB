using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Api.WorkCenter;
using NGB.Application.Abstractions.Services;
using NGB.Runtime.WorkCenter;
using Xunit;

namespace NGB.Runtime.Tests.WorkCenter;

public sealed class WorkCenterHealthAndRegistrationCoverageTests
{
    [Theory]
    [InlineData(0, 0, HealthStatus.Healthy)]
    [InlineData(1, 0, HealthStatus.Degraded)]
    [InlineData(1, 901, HealthStatus.Unhealthy)]
    public async Task Outbox_health_reports_healthy_degraded_and_stale_states(
        long failedCount,
        double oldestAgeSeconds,
        HealthStatus expected)
    {
        var snapshot = new WorkCenterOperationalHealthSnapshot(3, failedCount, oldestAgeSeconds, 5, 2);
        var reader = new Mock<IWorkCenterOperationalHealthReader>(MockBehavior.Strict);
        reader.Setup(candidate => candidate.ReadAsync(CancellationToken.None)).ReturnsAsync(snapshot);
        using var provider = new ServiceCollection().AddSingleton(reader.Object).BuildServiceProvider();
        var sut = new WorkCenterOutboxHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());

        var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.Should().Be(expected);
        result.Data.Should().Contain("pendingCount", 3L)
            .And.Contain("failedCount", failedCount)
            .And.Contain("oldestPendingAgeSeconds", oldestAgeSeconds)
            .And.Contain("openTaskCount", 5L)
            .And.Contain("overdueTaskCount", 2L);
        reader.VerifyAll();
    }

    [Fact]
    public async Task Work_center_extensions_register_health_realtime_hosting_options_and_hub_endpoint()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Ngb:WorkCenter:ProjectionBatchSize"] = "10"
            }).Build();
        var services = new ServiceCollection();

        services.AddHealthChecks().AddNgbWorkCenterHealth();
        services.AddNgbWorkCenterRealtime();
        services.AddNgbWorkCenterOutboxProcessing(configuration);
        services.AddNgbWorkCenterOutboxProcessing();

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IWorkCenterRealtimeNotifier)
            && descriptor.ImplementationType == typeof(SignalRWorkCenterRealtimeNotifier));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(WorkCenterOutboxHostedService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<NgbWorkCenterOptions>));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<NgbWorkCenterHostingOptions>));

        using (var provider = services.BuildServiceProvider())
        {
            var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
                .Value.Registrations;
            registrations.Should().ContainSingle(registration => registration.Name == "Work Center outbox");
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddNgbWorkCenterRealtime();
        var app = builder.Build();
        app.MapNgbWorkCenterHub().Should().BeSameAs(app);
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Should().Contain(endpoint => endpoint.DisplayName!.Contains("/hubs/work-center", StringComparison.Ordinal));
        await app.DisposeAsync();
    }
}
