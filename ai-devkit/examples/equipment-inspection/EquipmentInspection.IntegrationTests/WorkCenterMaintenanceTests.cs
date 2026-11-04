using EquipmentInspection.Definitions;
using EquipmentInspection.PostgreSql;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Application.Abstractions.Services;
using NGB.Core.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents.Actions;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.UnitOfWork;
using NGB.Runtime.WorkCenter;
using Testcontainers.PostgreSql;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class WorkCenterMaintenanceTests
{
    [Fact]
    public async Task Completed_action_receipts_expire_without_removing_recent_or_unfinished_receipts()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + postgres.GetConnectionString()]));
        var clock = new MaintenanceClock();
        await using var services = Services(postgres, clock);
        Guid document;
        await using (var scope = services.CreateAsyncScope())
        {
            var draft = await scope.ServiceProvider.GetRequiredService<IDocumentService>().CreateDraftAsync(
                EquipmentDefinitions.InspectionCode, InspectionRequests.Payload(), CancellationToken.None);
            document = draft.Id;
        }
        var old = await ReceiptAsync(services, document, "old", clock, complete: true);
        var unfinished = await ReceiptAsync(services, document, "unfinished", clock);
        clock.Advance(TimeSpan.FromDays(2));
        var recent = await ReceiptAsync(services, document, "recent", clock, complete: true);
        Assert.Equal(1, await PruneAsync(services));
        Assert.Equal(0, await PruneAsync(services));
        var renewed = await ReceiptAsync(services, document, "old", clock);
        Assert.Equal(DocumentActionExecutionBeginStatus.Begun, renewed.Status);
        Assert.NotEqual(old.ExecutionId, renewed.ExecutionId);
        var running = await ReceiptAsync(services, document, "unfinished", clock);
        Assert.Equal(DocumentActionExecutionBeginStatus.InProgress, running.Status);
        Assert.Equal(unfinished.ExecutionId, running.ExecutionId);
        var saved = await ReceiptAsync(services, document, "recent", clock);
        Assert.Equal(DocumentActionExecutionBeginStatus.Completed, saved.Status);
        Assert.Equal(recent.ExecutionId, saved.ExecutionId);
        Assert.NotNull(saved.ResultJson);
    }

    [Fact]
    public async Task Retention_removes_old_terminal_tasks_in_bounded_runs_and_preserves_open_or_recent_tasks()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + postgres.GetConnectionString()]));
        var clock = new MaintenanceClock();
        await using var services = Services(postgres, clock);
        var user = await CreateUserAsync(services);
        var source = new WorkCenterSourceReference("document", EquipmentDefinitions.InspectionCode,
            Guid.CreateVersion7(), "Retention fixture", null);
        var oldIds = new List<Guid>();
        for (var index = 0; index < 4; index++)
        {
            var key = "old-terminal-" + index;
            oldIds.Add(await CreateTaskAsync(services, user, source, key));
            await using var scope = services.CreateAsyncScope();
            var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskService>();
            if (index == 3)
                await tasks.CancelByDeduplicationKeyAsync(InspectionWorkCenterDefinitions.ReviewCode, key, CancellationToken.None);
            else
                await tasks.CompleteByDeduplicationKeyAsync(InspectionWorkCenterDefinitions.ReviewCode, key, CancellationToken.None);
        }

        var open = await CreateTaskAsync(services, user, source, "old-open");
        clock.Advance(TimeSpan.FromDays(2));
        var recent = await CreateTaskAsync(services, user, source, "recent-terminal");
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IWorkCenterTaskService>()
                .CompleteByDeduplicationKeyAsync(InspectionWorkCenterDefinitions.ReviewCode, "recent-terminal", CancellationToken.None);
        }

        Assert.Equal(5, (await ItemsAsync(services, user, WorkCenterQueryView.Completed, clock)).Count);
        Assert.Equal(2, await PruneAsync(services));
        var remaining = await ItemsAsync(services, user, WorkCenterQueryView.Completed, clock);
        Assert.Equal(3, remaining.Count);
        Assert.Contains(remaining, item => item.Id == recent);
        Assert.Equal(2, remaining.Count(item => oldIds.Contains(item.Id)));
        Assert.Equal(2, await PruneAsync(services));
        Assert.Equal(recent, Assert.Single(await ItemsAsync(services, user, WorkCenterQueryView.Completed, clock)).Id);
        Assert.Equal(open, Assert.Single(await ItemsAsync(services, user, WorkCenterQueryView.Tasks, clock)).Id);
        Assert.Equal(0, await PruneAsync(services));
        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, await PruneAsync(services));
        Assert.Empty(await ItemsAsync(services, user, WorkCenterQueryView.Completed, clock));
        Assert.Equal(open, Assert.Single(await ItemsAsync(services, user, WorkCenterQueryView.Tasks, clock)).Id);
    }

    [Fact]
    public async Task Expired_notification_deliveries_are_removed_without_losing_live_notifications()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + postgres.GetConnectionString()]));
        var clock = new MaintenanceClock();
        await using var services = Services(postgres, clock);
        var user = await CreateUserAsync(services);
        var source = new WorkCenterSourceReference("document", EquipmentDefinitions.InspectionCode,
            Guid.CreateVersion7(), "Notification retention fixture", null);
        var expired = await NotifyAsync(services, user, source, "expired", clock.GetUtcNow().UtcDateTime.AddHours(1));
        var live = await NotifyAsync(services, user, source, "live", null);
        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(2, await PruneAsync(services));
        var items = await ItemsAsync(services, user, WorkCenterQueryView.Notifications, clock);
        Assert.Equal(live, Assert.Single(items).Id);
        Assert.DoesNotContain(items, item => item.Id == expired);
        Assert.Equal(0, await PruneAsync(services));
    }

    [Fact]
    public async Task Outbox_retention_keeps_pending_processing_and_failed_deliveries_until_terminal()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + postgres.GetConnectionString()]));
        var clock = new MaintenanceClock();
        await using var services = Services(postgres, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        await using (var scope = services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(async ct =>
            {
                foreach (var consumer in new[] { "pending", "processing", "failed", "completed", "dead-letter" })
                {
                    var envelope = new OutboxEventEnvelope(Guid.CreateVersion7(), "example.retention.v1", 1,
                        now, "equipment-retention-test", consumer, null, Guid.CreateVersion7(), null, "{}", now);
                    await repository.AppendAsync(envelope, [consumer], ct);
                    if (consumer == "pending")
                        continue;
                    var claimed = Assert.Single(await repository.ClaimBatchAsync(consumer, 1, now, ct));
                    if (consumer == "completed")
                        await repository.MarkCompletedAsync(envelope.EventId, consumer, claimed.AttemptCount, now, ct);
                    else if (consumer is "failed" or "dead-letter")
                        await repository.MarkFailedAsync(envelope.EventId, consumer, claimed.AttemptCount, now,
                            now.AddDays(3), "Fixture failure", consumer == "dead-letter", ct);
                }
            });
        }

        clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(2, await PruneAsync(services));
        Assert.Equal(0, await PruneAsync(services));
        await using (var scope = services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            foreach (var consumer in new[] { "pending", "failed" })
            {
                Assert.Equal(1, (await repository.GetHealthAsync(consumer, CancellationToken.None)).PendingCount);
            }
            Assert.Equal(0, (await repository.GetHealthAsync("dead-letter", CancellationToken.None)).FailedCount);
        }

        clock.Advance(TimeSpan.FromDays(2));
        await using (var scope = services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxEventRepository>();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(async ct =>
            {
                foreach (var consumer in new[] { "pending", "processing", "failed" })
                {
                    var claimed = Assert.Single(await repository.ClaimBatchAsync(consumer, 1, clock.GetUtcNow().UtcDateTime, ct));
                    await repository.MarkCompletedAsync(claimed.Event.EventId, consumer, claimed.AttemptCount,
                        clock.GetUtcNow().UtcDateTime, ct);
                }
            });
        }
        Assert.Equal(2, await PruneAsync(services));
        Assert.Equal(1, await PruneAsync(services));
        Assert.Equal(0, await PruneAsync(services));
    }

    private static ServiceProvider Services(PostgreSqlContainer postgres, MaintenanceClock clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNgbRuntime().AddNgbPostgres(postgres.GetConnectionString()).AddEquipmentRuntime();
        services.AddEquipmentPersistence();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOptions<NgbWorkCenterOptions>>(Options.Create(new NgbWorkCenterOptions
        {
            DocumentActionExecutionRetention = TimeSpan.FromDays(1),
            TerminalTaskRetention = TimeSpan.FromDays(1),
            NotificationDeliveryRetention = TimeSpan.FromDays(1),
            OutboxRetention = TimeSpan.FromDays(1),
            MaintenanceBatchSize = 1,
            MaximumMaintenanceBatchesPerRun = 2
        }));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<Guid> CreateUserAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(ct =>
            scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>()
                .UpsertAsync("retention-user", "retention@example.test", "Retention recipient", true, ct));
    }

    private static async Task<Guid> CreateTaskAsync(
        ServiceProvider services, Guid user, WorkCenterSourceReference source, string key)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkCenterTaskService>().CreateAsync(
            new CreateWorkCenterTaskRequest(InspectionWorkCenterDefinitions.ReviewCode, source, "Review inspection",
                null, WorkCenterPriority.Normal, user, null, null, null, null, key, null, null), CancellationToken.None);
        return result.ItemId!.Value;
    }

    private static async Task<Guid> NotifyAsync(
        ServiceProvider services, Guid user, WorkCenterSourceReference source, string key, DateTime? expires)
    {
        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<INotificationService>().CreateAsync(
            new CreateNotificationRequest(InspectionWorkCenterDefinitions.NotificationCode, source, "Inspection update",
                null, null, [user], expires, key, null, null), CancellationToken.None);
        return result.ItemId!.Value;
    }

    private static async Task<IReadOnlyList<WorkCenterItemRecord>> ItemsAsync(
        ServiceProvider services, Guid user, WorkCenterQueryView view, MaintenanceClock clock)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkCenterReadRepository>().GetItemsAsync(
            new WorkCenterQuery(user, [], true, [], [], null, 20, view, null, null, null, null, null,
                clock.GetUtcNow().UtcDateTime), CancellationToken.None);
    }

    private static async Task<int> PruneAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkCenterMaintenanceService>().PruneAsync(CancellationToken.None);
    }

    private static async Task<DocumentActionExecutionBeginResult> ReceiptAsync(
        ServiceProvider services, Guid document, string key, MaintenanceClock clock, bool complete = false)
    {
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentActionExecutionRepository>();
        return await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(async ct =>
        {
            var result = await repository.TryBeginAsync(key, new string('a', 64), document,
                EquipmentDefinitions.InspectionCode, "post", clock.GetUtcNow().UtcDateTime, ct);
            if (complete)
                await repository.MarkCompletedAsync(result.ExecutionId, "{}", clock.GetUtcNow().UtcDateTime, ct);
            return result;
        });
    }

    private sealed class MaintenanceClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 5, 17, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
