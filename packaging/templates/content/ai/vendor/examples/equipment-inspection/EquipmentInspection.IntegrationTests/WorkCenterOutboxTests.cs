using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.IntegrationEvents;
using NGB.Contracts.Metadata;
using NGB.Contracts.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.UnitOfWork;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class WorkCenterOutboxTests
{
    [Fact]
    public async Task Failed_projection_rolls_back_both_items_and_retries_the_committed_action_once()
    {
        var fault = new ProjectionFault();
        var realtime = new RealtimeProbe();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddScoped<IDocumentActionCompletedWorkCenterPolicy>(services =>
                new FaultPolicy(services.GetRequiredService<IWorkCenterReadRepository>(), fault));
            services.Replace(ServiceDescriptor.Singleton<IWorkCenterRealtimeNotifier>(realtime));
        });
        await host.StartAsync();
        var actor = await LinkActorAsync(host);
        using var client = await host.CreateClientAsync("application-administrator");
        realtime.ReadCommitted = async () =>
        {
            Assert.Single((await ItemsAsync(client, WorkCenterTab.Tasks)).Items);
            Assert.Single((await ItemsAsync(client, WorkCenterTab.Notifications)).Items);
        };
        var (path, version) = await CreateCompleteInspectionAsync(client);
        using var postResponse = await ExecutePostAsync(client, path, version, "outbox-post");
        var posted = await ReadActionAsync(postResponse);
        using var unpostResponse = await ExecuteActionAsync(client, path, "unpost", posted.DocumentVersion, "outbox-unpost");
        var unposted = await ReadActionAsync(unpostResponse);

        var completed = await fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(actor, completed.ActorUserId);
        Assert.Equal(unposted.Document.Id, completed.Data.DocumentId);
        Assert.Equal(unposted.DocumentVersion, completed.Data.DocumentVersion);
        Assert.Equal(DocumentStatus.Posted, completed.Data.PreviousStatus);
        Assert.Equal(DocumentStatus.Draft, completed.Data.CurrentStatus);
        Assert.Empty((await ItemsAsync(client, WorkCenterTab.Tasks)).Items);
        Assert.Empty((await ItemsAsync(client, WorkCenterTab.Notifications)).Items);
        Assert.Empty(realtime.Deliveries);
        fault.Release.TrySetResult();

        await EventuallyAsync(async () => (await HealthAsync(host)).FailedDeliveryCount == 1);
        var failed = await HealthAsync(host);
        Assert.Equal(1, failed.PendingDeliveryCount);
        Assert.Empty((await ItemsAsync(client, WorkCenterTab.Tasks)).Items);
        Assert.Empty((await ItemsAsync(client, WorkCenterTab.Notifications)).Items);
        Assert.Empty(realtime.Deliveries);
        Volatile.Write(ref fault.Allow, true);

        var delivery = await realtime.First.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(new[] { actor }, delivery.Users);
        Assert.True(delivery.Version > 0);
        await EventuallyAsync(async () => (await HealthAsync(host)).PendingDeliveryCount == 0);
        Assert.Equal(0, (await HealthAsync(host)).FailedDeliveryCount);
        Assert.True(fault.Attempts >= 2);
        Assert.Single(fault.Events.Distinct());
        var task = Assert.Single((await ItemsAsync(client, WorkCenterTab.Tasks)).Items);
        var notification = Assert.Single((await ItemsAsync(client, WorkCenterTab.Notifications)).Items);
        Assert.Equal(completed.Data.DocumentId, task.Source.EntityId);
        Assert.Equal(completed.Data.DocumentId, notification.Source.EntityId);
        Assert.Equal(actor, task.Assignment!.AssignedUserId);
        Assert.Equal("Review withdrawn inspection", task.Title);

        using var retry = await ExecuteActionAsync(client, path, "unpost", posted.DocumentVersion, "outbox-unpost");
        Assert.Equal(unposted.ExecutionId, (await ReadActionAsync(retry)).ExecutionId);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var policy = services.GetServices<IDocumentActionCompletedWorkCenterPolicy>()
                .OfType<InspectionWithdrawalPolicy>().Single();
            var changed = await services.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(
                ct => policy.HandleAsync(completed, ct));
            Assert.Empty(changed);
            Assert.Equal(0, await services.GetRequiredService<IOutboxProcessor>().ProcessBatchAsync(25, CancellationToken.None));
        }
        Assert.Equal(task.Id, Assert.Single((await ItemsAsync(client, WorkCenterTab.Tasks)).Items).Id);
        Assert.Equal(notification.Id, Assert.Single((await ItemsAsync(client, WorkCenterTab.Notifications)).Items).Id);
        Assert.Single(realtime.Deliveries);
    }

    [Fact]
    public async Task Rolled_back_event_is_invisible_and_realtime_failure_does_not_undo_a_committed_projection()
    {
        var realtime = new RealtimeProbe { Fail = true };
        await using var host = new InspectionHost(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWorkCenterRealtimeNotifier>(realtime)));
        await host.StartAsync();
        var actor = await LinkActorAsync(host);
        using var client = await host.CreateClientAsync("application-administrator");
        realtime.ReadCommitted = async () =>
        {
            Assert.Single((await ItemsAsync(client, WorkCenterTab.Tasks)).Items);
            Assert.Single((await ItemsAsync(client, WorkCenterTab.Notifications)).Items);
        };
        var (path, _) = await CreateCompleteInspectionAsync(client);
        var document = Guid.Parse(path[(path.LastIndexOf('/') + 1)..]);
        var now = DateTime.UtcNow;
        // A test-owned integration event isolates the outbox transaction boundary.
        // The preceding test separately verifies the event emitted by the real dispatcher.
        var completed = new DocumentActionCompletedV1(Guid.CreateVersion7(), now, "equipment-contract-test",
            "document:" + document, actor, Guid.CreateVersion7(), null,
            new DocumentActionCompletedDataV1(document, EquipmentDefinitions.InspectionCode, "unpost",
                DocumentStatus.Posted, DocumentStatus.Draft, 2));
        var envelope = new OutboxEventEnvelope(completed.EventId, completed.Type, completed.PayloadSchemaVersion,
            now, completed.Source, completed.Subject, actor, completed.CorrelationId, null,
            JsonSerializer.Serialize(completed, Json), now);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var transaction = services.GetRequiredService<IUnitOfWork>();
            await transaction.BeginTransactionAsync();
            try
            {
                await services.GetRequiredService<IOutboxEventRepository>().AppendAsync(envelope, ["work-center"], CancellationToken.None);
                Assert.Equal(0, (await HealthAsync(host)).PendingDeliveryCount);
                Assert.Empty((await ItemsAsync(client, WorkCenterTab.Tasks)).Items);
                Assert.Empty(realtime.Deliveries);
            }
            finally
            {
                await transaction.RollbackAsync();
            }
        }
        Assert.Equal(0, (await HealthAsync(host)).PendingDeliveryCount);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(ct =>
                services.GetRequiredService<IOutboxEventRepository>().AppendAsync(envelope, ["work-center"], ct));
        }

        await realtime.First.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await EventuallyAsync(async () => (await HealthAsync(host)).PendingDeliveryCount == 0);
        Assert.Equal(0, (await HealthAsync(host)).FailedDeliveryCount);
        Assert.Single((await ItemsAsync(client, WorkCenterTab.Tasks)).Items);
        Assert.Single((await ItemsAsync(client, WorkCenterTab.Notifications)).Items);
        await using var check = host.Services.CreateAsyncScope();
        Assert.Equal(0, await check.ServiceProvider.GetRequiredService<IOutboxProcessor>()
            .ProcessBatchAsync(25, CancellationToken.None));
        Assert.Single(realtime.Deliveries);
    }

    private static async Task<Guid> LinkActorAsync(InspectionHost host)
    {
        await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode, "Inspection administrators");
        await using var scope = host.Services.CreateAsyncScope();
        var users = await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>()
            .GetByEmailsAsync(["application-administrator@example.test"]);
        return Assert.Single(users).UserId;
    }

    private static async Task<WorkCenterPageDto> ItemsAsync(HttpClient client, WorkCenterTab tab)
        => await client.GetFromJsonAsync<WorkCenterPageDto>("/api/work-center/items?tab=" + tab + "&limit=10", Json)
            ?? throw new InvalidOperationException("Missing Work Center page.");

    private static async Task<WorkCenterOperationalHealthSnapshot> HealthAsync(InspectionHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkCenterOperationalHealthReader>().ReadAsync(CancellationToken.None);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition())
            await Task.Delay(25, timeout.Token);
    }

    private sealed class ProjectionFault
    {
        public TaskCompletionSource<DocumentActionCompletedV1> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<Guid> Events { get; } = new();
        public bool Allow;
        public int Attempts;
    }

    private sealed class FaultPolicy(IWorkCenterReadRepository reader, ProjectionFault fault) : IDocumentActionCompletedWorkCenterPolicy
    {
        public async Task<IReadOnlyList<Guid>> HandleAsync(DocumentActionCompletedV1 @event, CancellationToken ct)
        {
            if (@event.Data.ActionCode != "unpost")
                return [];

            var query = new WorkCenterQuery(@event.ActorUserId!.Value, [], true, [], [], null, 10,
                WorkCenterQueryView.Tasks, null, null, null, null, null, DateTime.UtcNow);
            Assert.Single(await reader.GetItemsAsync(query, ct));
            Assert.Single(await reader.GetItemsAsync(query with { View = WorkCenterQueryView.Notifications }, ct));
            fault.Events.Enqueue(@event.EventId);
            Interlocked.Increment(ref fault.Attempts);
            fault.Entered.TrySetResult(@event);
            await fault.Release.Task.WaitAsync(ct);
            if (!Volatile.Read(ref fault.Allow))
                throw new InvalidOperationException("Injected failure after both Work Center mutations.");
            return [];
        }
    }

    private sealed record Delivery(long Version, Guid[] Users);

    private sealed class RealtimeProbe : IWorkCenterRealtimeNotifier
    {
        public TaskCompletionSource<Delivery> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<Delivery> Deliveries { get; } = new();
        public bool Fail { get; init; }
        public Func<Task>? ReadCommitted { get; set; }

        public async Task NotifyUsersChangedAsync(long version, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
        {
            var delivery = new Delivery(version, userIds.ToArray());
            Deliveries.Enqueue(delivery);
            try
            {
                if (ReadCommitted is not null)
                    await ReadCommitted();
            }
            catch (Exception exception)
            {
                First.TrySetException(exception);
                throw;
            }
            First.TrySetResult(delivery);
            if (Fail)
                throw new InvalidOperationException("Injected realtime failure after commit.");
        }
    }
}
