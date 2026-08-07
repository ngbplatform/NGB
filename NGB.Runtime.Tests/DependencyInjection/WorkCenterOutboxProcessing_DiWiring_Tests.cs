using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Api.WorkCenter;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.Runtime.Tests.DependencyInjection;

public sealed class WorkCenterOutboxProcessing_DiWiring_Tests
{
    [Fact]
    public void AddNgbRuntime_does_not_start_the_outbox_processor_implicitly()
    {
        var services = new ServiceCollection();

        services.AddNgbRuntime();

        WorkCenterHostedServices(services).Should().BeEmpty();
    }

    [Fact]
    public void AddNgbWorkCenterOutboxProcessing_registers_exactly_one_processor_when_called_repeatedly()
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();

        services.AddNgbWorkCenterOutboxProcessing();
        services.AddNgbWorkCenterOutboxProcessing();

        WorkCenterHostedServices(services).Should().ContainSingle();
    }

    private static IEnumerable<ServiceDescriptor> WorkCenterHostedServices(IServiceCollection services)
        => services.Where(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.FullName == "NGB.Api.WorkCenter.WorkCenterOutboxHostedService");
}
