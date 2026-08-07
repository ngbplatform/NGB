using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Persistence.Outbox;
using NGB.Runtime.WorkCenter;
using Xunit;

namespace NGB.Runtime.Tests.WorkCenter;

public sealed class WorkCenterOutboxHostedServiceTests
{
    [Fact]
    public async Task Hosted_service_stops_when_outbox_storage_is_not_registered()
    {
        var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<IServiceProvider>(MockBehavior.Strict);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IOutboxEventRepository)))
            .Returns((object)null!);
        var scope = Scope(provider.Object);
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopes.Setup(factory => factory.CreateScope())
            .Callback(() => tick.TrySetResult())
            .Returns(scope.Object);
        var service = new WorkCenterOutboxHostedService(
            scopes.Object,
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
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
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
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IOutboxEventRepository)))
            .Returns(outbox.Object);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IOutboxProcessor)))
            .Returns(processor.Object);
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopes.Setup(factory => factory.CreateScope()).Returns(Scope(provider.Object).Object);
        var service = new WorkCenterOutboxHostedService(
            scopes.Object,
            NullLogger<WorkCenterOutboxHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        processor.Verify(
            candidate => candidate.ProcessBatchAsync(100, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Hosted_service_contains_transient_processor_failures_until_the_next_tick()
    {
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var processor = new Mock<IOutboxProcessor>(MockBehavior.Strict);
        processor.Setup(candidate => candidate.ProcessBatchAsync(100, It.IsAny<CancellationToken>()))
            .Callback(() => failed.TrySetResult())
            .ThrowsAsync(new InvalidOperationException("temporary database failure"));
        var provider = new Mock<IServiceProvider>(MockBehavior.Strict);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IOutboxEventRepository)))
            .Returns(outbox.Object);
        provider.Setup(serviceProvider => serviceProvider.GetService(typeof(IOutboxProcessor)))
            .Returns(processor.Object);
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        scopes.Setup(factory => factory.CreateScope()).Returns(Scope(provider.Object).Object);
        var service = new WorkCenterOutboxHostedService(
            scopes.Object,
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
            NullLogger<WorkCenterOutboxHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        scopes.Verify(factory => factory.CreateScope(), Times.Never);
    }

    [Fact]
    public async Task Hosted_service_disposes_async_only_scoped_dependencies_asynchronously()
    {
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        var services = new ServiceCollection();
        services.AddSingleton(outbox.Object);
        services.AddScoped(_ => new AsyncOnlyDependency(disposed));
        services.AddScoped<IOutboxProcessor>(provider => new AsyncOnlyOutboxProcessor(
            provider.GetRequiredService<AsyncOnlyDependency>(),
            processed));
        await using var provider = services.BuildServiceProvider();
        var service = new WorkCenterOutboxHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
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

    private sealed class AsyncOnlyDependency(TaskCompletionSource disposed) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncOnlyOutboxProcessor(
        AsyncOnlyDependency dependency,
        TaskCompletionSource processed)
        : IOutboxProcessor
    {
        private readonly AsyncOnlyDependency _dependency = dependency;

        public Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct)
        {
            _ = _dependency;
            processed.TrySetResult();
            return Task.FromResult(0);
        }
    }
}
