using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Api.WorkCenter;
using NGB.Application.Abstractions.Services;
using Xunit;

namespace NGB.Runtime.Tests.WorkCenter;

public sealed class WorkCenterOutboxHostedServiceTests
{
    [Fact]
    public async Task Hosted_service_contains_missing_processor_registration_until_the_next_tick()
    {
        var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<IServiceProvider>(MockBehavior.Strict);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IOutboxProcessor)))
            .Returns((object)null!);
        var scope = Scope(provider.Object);
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopes.Setup(factory => factory.CreateScope())
            .Callback(() => tick.TrySetResult())
            .Returns(scope.Object);
        var service = new WorkCenterOutboxHostedService(
            scopes.Object,
            TimeProvider.System,
            Options(),
            NullLogger<WorkCenterOutboxHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await tick.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        scopes.Verify(factory => factory.CreateScope(), Times.Once);
        scope.Verify(candidate => candidate.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Hosted_service_drains_ready_work_in_bounded_batches()
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new Mock<IOutboxProcessor>(MockBehavior.Strict);
        var calls = 0;
        processor.Setup(candidate => candidate.ProcessBatchAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                calls++;
                if (calls == 2)
                {
                    drained.TrySetResult();
                    return 0;
                }
                return 1;
            });
        var provider = new Mock<IServiceProvider>(MockBehavior.Strict);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IOutboxProcessor)))
            .Returns(processor.Object);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IWorkCenterMaintenanceService)))
            .Returns(Maintenance().Object);
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopes.Setup(factory => factory.CreateScope()).Returns(Scope(provider.Object).Object);
        var service = new WorkCenterOutboxHostedService(
            scopes.Object,
            TimeProvider.System,
            Options(),
            NullLogger<WorkCenterOutboxHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        processor.Verify(
            candidate => candidate.ProcessBatchAsync(100, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Hosted_service_polls_again_on_the_next_tick_and_logs_successful_maintenance()
    {
        var secondPoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new Mock<IOutboxProcessor>(MockBehavior.Strict);
        var calls = 0;
        processor.Setup(candidate => candidate.ProcessBatchAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (Interlocked.Increment(ref calls) == 2)
                    secondPoll.TrySetResult();
                return 0;
            });
        var maintenance = new Mock<IWorkCenterMaintenanceService>(MockBehavior.Strict);
        maintenance.Setup(service => service.PruneAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        var provider = new Mock<IServiceProvider>(MockBehavior.Strict);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IOutboxProcessor)))
            .Returns(processor.Object);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IWorkCenterMaintenanceService)))
            .Returns(maintenance.Object);
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopes.Setup(factory => factory.CreateScope()).Returns(Scope(provider.Object).Object);
        var logger = new RecordingLogger();
        var service = new WorkCenterOutboxHostedService(
            scopes.Object,
            TimeProvider.System,
            Options(),
            logger);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await secondPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        processor.Verify(
            candidate => candidate.ProcessBatchAsync(100, It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
        logger.Messages.Should().ContainSingle("Pruned 3 expired Work Center records.");
    }

    [Fact]
    public async Task Hosted_service_contains_transient_processor_failures_until_the_next_tick()
    {
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new Mock<IOutboxProcessor>(MockBehavior.Strict);
        processor.Setup(candidate => candidate.ProcessBatchAsync(100, It.IsAny<CancellationToken>()))
            .Callback(() => failed.TrySetResult())
            .ThrowsAsync(new InvalidOperationException("temporary database failure"));
        var provider = new Mock<IServiceProvider>(MockBehavior.Strict);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IOutboxProcessor)))
            .Returns(processor.Object);
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopes.Setup(factory => factory.CreateScope()).Returns(Scope(provider.Object).Object);
        var service = new WorkCenterOutboxHostedService(
            scopes.Object,
            TimeProvider.System,
            Options(),
            NullLogger<WorkCenterOutboxHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        processor.Verify(
            candidate => candidate.ProcessBatchAsync(100, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Hosted_service_treats_host_cancellation_as_graceful_shutdown()
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var service = new WorkCenterOutboxHostedService(
            scopes.Object,
            TimeProvider.System,
            Options(),
            NullLogger<WorkCenterOutboxHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Hosted_service_disposes_async_only_scoped_dependencies_asynchronously()
    {
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddScoped(_ => new AsyncOnlyDependency(disposed));
        services.AddScoped<IOutboxProcessor>(provider => new AsyncOnlyOutboxProcessor(
            provider.GetRequiredService<AsyncOnlyDependency>(),
            processed));
        services.AddScoped(_ => Maintenance().Object);
        await using var provider = services.BuildServiceProvider();
        var service = new WorkCenterOutboxHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options(),
            NullLogger<WorkCenterOutboxHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await processed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static Mock<IServiceScope> Scope(IServiceProvider provider)
    {
        var scope = new Mock<IServiceScope>(MockBehavior.Loose);
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(provider);
        return scope;
    }

    [Fact]
    public void Hosting_options_validation_accepts_defaults_and_rejects_unsafe_polling_values()
    {
        var validator = new NgbWorkCenterHostingOptionsValidator();

        validator.Validate(null, new NgbWorkCenterHostingOptions()).Succeeded.Should().BeTrue();
        var invalid = validator.Validate(null, new NgbWorkCenterHostingOptions
        {
            PollInterval = TimeSpan.Zero,
            MaintenanceInterval = TimeSpan.FromDays(8),
            ProjectionBatchSize = 101
        });

        invalid.Failed.Should().BeTrue();
        invalid.Failures.Should().Contain(message => message.Contains(nameof(NgbWorkCenterHostingOptions.PollInterval)));
        invalid.Failures.Should().Contain(message => message.Contains(nameof(NgbWorkCenterHostingOptions.MaintenanceInterval)));
        invalid.Failures.Should().Contain(message => message.Contains(nameof(NgbWorkCenterHostingOptions.ProjectionBatchSize)));

        validator.Validate(null, new NgbWorkCenterHostingOptions { ProjectionBatchSize = 0 })
            .Failures.Should()
            .ContainSingle(message => message.Contains(nameof(NgbWorkCenterHostingOptions.ProjectionBatchSize)));
    }

    private static IOptions<NgbWorkCenterHostingOptions> Options()
        => Microsoft.Extensions.Options.Options.Create(new NgbWorkCenterHostingOptions
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            MaintenanceInterval = TimeSpan.FromHours(6),
            ProjectionBatchSize = 100
        });

    private static Mock<IWorkCenterMaintenanceService> Maintenance()
    {
        var maintenance = new Mock<IWorkCenterMaintenanceService>(MockBehavior.Strict);
        maintenance.Setup(service => service.PruneAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        return maintenance;
    }

    private sealed class AsyncOnlyDependency(TaskCompletionSource disposed) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncOnlyOutboxProcessor(AsyncOnlyDependency dependency, TaskCompletionSource processed)
        : IOutboxProcessor
    {
        public Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct)
        {
            _ = dependency;
            processed.TrySetResult();
            return Task.FromResult(0);
        }
    }

    private sealed class RecordingLogger : ILogger<WorkCenterOutboxHostedService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
                Messages.Add(formatter(state, exception));
        }
    }
}
