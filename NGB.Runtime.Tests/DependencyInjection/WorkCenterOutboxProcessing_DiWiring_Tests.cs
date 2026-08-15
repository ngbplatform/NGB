using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NGB.Api.WorkCenter;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.WorkCenter;
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

    [Fact]
    public void AddNgbWorkCenterOutboxProcessing_binds_hosting_and_runtime_policies_to_distinct_option_types()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ngb:WorkCenter:PollInterval"] = "00:00:03",
                ["Ngb:WorkCenter:ProjectionBatchSize"] = "17",
                ["Ngb:WorkCenter:DocumentActionExecutionRetention"] = "12.00:00:00"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddNgbRuntime();

        services.AddNgbWorkCenterOutboxProcessing(configuration);
        using var provider = services.BuildServiceProvider();

        var hosting = provider.GetRequiredService<IOptions<NgbWorkCenterHostingOptions>>().Value;
        var runtime = provider.GetRequiredService<IOptions<NgbWorkCenterOptions>>().Value;
        hosting.PollInterval.Should().Be(TimeSpan.FromSeconds(3));
        hosting.ProjectionBatchSize.Should().Be(17);
        runtime.DocumentActionExecutionRetention.Should().Be(TimeSpan.FromDays(12));
    }

    private static IEnumerable<ServiceDescriptor> WorkCenterHostedServices(IServiceCollection services)
        => services.Where(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.FullName == "NGB.Api.WorkCenter.WorkCenterOutboxHostedService");
}
