using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Services;
using NGB.Contracts.WorkCenter;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using NGB.Definitions.WorkCenter;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;
using CoreChannel = NGB.Core.WorkCenter.NotificationChannel;
using CoreKind = NGB.Core.WorkCenter.WorkCenterPreferenceKind;
using CorePriority = NGB.Core.WorkCenter.WorkCenterPriority;
using CoreSeverity = NGB.Core.WorkCenter.NotificationSeverity;
using NotificationChannel = NGB.Contracts.WorkCenter.NotificationChannel;
using WorkCenterTaskStatus = NGB.Contracts.WorkCenter.WorkCenterTaskStatus;

namespace EquipmentInspection.IntegrationTests;

public sealed class WorkCenterHttpTests
{
    private const string TaskCode = "example.inspection-review";
    private const string NotificationCode = "example.inspection-completed";
    private const string MandatoryCode = "example.inspection-required";

    [Fact]
    public async Task Notification_preferences_deduplicate_delivery_and_preserve_each_users_read_state()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        var (first, second) = await LinkRecipientsAsync(host);
        using var admin = await host.CreateClientAsync("administrator");
        using var other = await host.CreateClientAsync("application-administrator");
        var source = await CreateSourceAsync(admin);
        var request = Notification(source, [first, second], "inspection-notification");
        var created = await NotifyAsync(host, request);
        Assert.NotNull(created.ItemId);
        Assert.Equal(new[] { first, second }.Order(), created.ChangedUserIds.Order());
        var repeated = await NotifyAsync(host, request);
        Assert.Equal(created.ItemId, repeated.ItemId);
        Assert.Empty(repeated.ChangedUserIds);
        var item = Assert.Single((await ItemsAsync(admin, WorkCenterTab.Notifications)).Items);
        Assert.Equal(created.ItemId, item.Id);
        Assert.Equal(source.EntityId, item.Source.EntityId);
        Assert.False(item.IsRead);
        Assert.Equal(1, (await SummaryAsync(admin)).UnreadNotificationCount);

        using var read = await admin.PostAsync($"/api/work-center/notifications/{item.Id}/read", null);
        await EnsureSuccessAsync(read);
        Assert.True(Assert.Single((await ItemsAsync(admin, WorkCenterTab.Notifications)).Items).IsRead);
        Assert.False(Assert.Single((await ItemsAsync(other, WorkCenterTab.Notifications)).Items).IsRead);
        using var dismissed = await admin.PostAsync($"/api/work-center/notifications/{item.Id}/dismiss", null);
        await EnsureSuccessAsync(dismissed);
        Assert.Empty((await ItemsAsync(admin, WorkCenterTab.Notifications)).Items);
        Assert.Single((await ItemsAsync(other, WorkCenterTab.Notifications)).Items);

        await SetPreferenceAsync(admin, NotificationCode, false);
        var preferences = await admin.GetFromJsonAsync<NotificationPreferenceDto[]>("/api/me/notification-preferences", Json);
        Assert.False(Assert.Single(preferences!, row => row.Code == NotificationCode).IsEnabled);
        var roleDelivery = await NotifyAsync(host, Notification(source, [], "role-notification") with
        {
            RecipientRoleCode = InspectionHost.AdministratorRoleCode
        });
        Assert.Equal(new[] { second }, roleDelivery.ChangedUserIds);
        Assert.Empty((await ItemsAsync(admin, WorkCenterTab.Notifications)).Items);
        Assert.Equal(2, (await ItemsAsync(other, WorkCenterTab.Notifications)).Items.Count);
        using var mandatory = await admin.PutAsJsonAsync("/api/me/notification-preferences",
            new UpdateNotificationPreferencesRequestDto([new(MandatoryCode, NotificationChannel.InApp, false)]));
        Assert.Equal(HttpStatusCode.BadRequest, mandatory.StatusCode);
        preferences = await admin.GetFromJsonAsync<NotificationPreferenceDto[]>("/api/me/notification-preferences", Json);
        Assert.True(Assert.Single(preferences!, row => row.Code == MandatoryCode).IsEnabled);

        var expected = new List<Guid> { created.ItemId.Value, roleDelivery.ItemId!.Value };
        for (var index = 0; index < 3; index++)
        {
            var delivery = await NotifyAsync(host, Notification(source, [second], "paged-" + index));
            expected.Add(delivery.ItemId!.Value);
        }
        var listed = new List<Guid>();
        var cursors = new HashSet<string>();
        string? cursor = null;
        do
        {
            var page = await other.GetFromJsonAsync<WorkCenterPageDto>(
                "/api/work-center/items?tab=Notifications&limit=2&cursor=" + Uri.EscapeDataString(cursor ?? ""), Json);
            Assert.InRange(page!.Items.Count, 0, 2);
            Assert.Equal(2, page.Limit);
            listed.AddRange(page.Items.Select(row => row.Id));
            cursor = page.NextCursor;
            if (cursor is not null)
                Assert.True(cursors.Add(cursor));
            Assert.InRange(listed.Count, 0, 5);
        } while (cursor is not null);
        Assert.Equal(expected.Order(), listed.Order());

        await using var scope = host.Services.CreateAsyncScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => notifications.CreateAsync(request with
        {
            RecipientRoleCode = InspectionHost.AdministratorRoleCode
        }, CancellationToken.None));
        using var anonymous = host.CreateAnonymousClient();
        using var anonymousRead = await anonymous.GetAsync("/api/work-center/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
    }

    [Fact]
    public async Task Role_task_claim_is_concurrent_and_completion_preserves_the_completed_item()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        var (first, second) = await LinkRecipientsAsync(host);
        using var admin = await host.CreateClientAsync("administrator");
        using var other = await host.CreateClientAsync("application-administrator");
        var source = await CreateSourceAsync(admin);
        var request = new CreateWorkCenterTaskRequest(TaskCode, source, "Review inspection", "Check the recorded result",
            CorePriority.High, null, InspectionHost.AdministratorRoleCode, DateTime.UtcNow.AddHours(-1),
            null, null, "review-inspection", Guid.CreateVersion7(), Guid.CreateVersion7());
        await using var scope = host.Services.CreateAsyncScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskService>();
        var created = await tasks.CreateAsync(request, CancellationToken.None);
        Assert.Equal(new[] { first, second }.Order(), created.ChangedUserIds.Order());
        var repeated = await tasks.CreateAsync(request, CancellationToken.None);
        Assert.Equal(created.ItemId, repeated.ItemId);
        Assert.Empty(repeated.ChangedUserIds);
        var before = Assert.Single((await ItemsAsync(admin, WorkCenterTab.Tasks)).Items);
        Assert.True(before.Assignment!.IsRoleAssigned);
        Assert.True(before.IsOverdue);
        Assert.Equal(1, (await SummaryAsync(other)).OpenTaskCount);

        var path = $"/api/work-center/tasks/{before.Id}";
        var claims = await Task.WhenAll(
            admin.PostAsJsonAsync(path + "/claim", new ClaimWorkCenterTaskRequestDto(before.Version)),
            other.PostAsJsonAsync(path + "/claim", new ClaimWorkCenterTaskRequestDto(before.Version)));
        using var firstResponse = claims[0];
        using var secondResponse = claims[1];
        Assert.Single(claims, response => response.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, Assert.Single(claims, response => !response.IsSuccessStatusCode).StatusCode);
        var winner = firstResponse.IsSuccessStatusCode ? admin : other;
        var winnerId = firstResponse.IsSuccessStatusCode ? first : second;
        var claimed = Assert.Single((await ItemsAsync(winner, WorkCenterTab.Tasks)).Items);
        Assert.Equal(winnerId, claimed.Assignment!.ClaimedByUserId);
        Assert.Equal(WorkCenterTaskStatus.InProgress, claimed.TaskStatus);
        Assert.True(claimed.Version > before.Version);
        using var stale = await winner.PostAsJsonAsync(path + "/claim", new ClaimWorkCenterTaskRequestDto(before.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var read = await winner.PostAsync(path + "/read", null);
        await EnsureSuccessAsync(read);
        using var snooze = await winner.PostAsJsonAsync(path + "/snooze", new SnoozeWorkCenterTaskRequestDto(DateTime.UtcNow.AddHours(1)));
        await EnsureSuccessAsync(snooze);
        Assert.Empty((await ItemsAsync(winner, WorkCenterTab.Attention)).Items);
        var snoozed = Assert.Single((await ItemsAsync(winner, WorkCenterTab.Tasks)).Items);
        Assert.True(snoozed.IsRead);
        Assert.NotNull(snoozed.SnoozedUntilUtc);

        var completed = await tasks.CompleteByDeduplicationKeyAsync(TaskCode, request.DeduplicationKey, CancellationToken.None);
        Assert.Contains(winnerId, completed);
        Assert.Empty(await tasks.CompleteByDeduplicationKeyAsync(TaskCode, request.DeduplicationKey, CancellationToken.None));
        Assert.Empty((await ItemsAsync(winner, WorkCenterTab.Tasks)).Items);
        var finished = Assert.Single((await ItemsAsync(winner, WorkCenterTab.Completed)).Items);
        Assert.Equal(before.Id, finished.Id);
        Assert.Equal(WorkCenterTaskStatus.Completed, finished.TaskStatus);

        var personal = await tasks.CreateAsync(request with
        {
            AssignedUserId = first, AssignedRoleCode = null, DeduplicationKey = "personal-review"
        }, CancellationToken.None);
        Assert.Equal(new[] { first }, personal.ChangedUserIds);
        Assert.Equal(personal.ItemId, Assert.Single((await ItemsAsync(admin, WorkCenterTab.Tasks)).Items).Id);
        Assert.Empty((await ItemsAsync(other, WorkCenterTab.Tasks)).Items);
        await tasks.CancelByDeduplicationKeyAsync(TaskCode, "personal-review", CancellationToken.None);
        Assert.Empty(await tasks.CancelByDeduplicationKeyAsync(TaskCode, "personal-review", CancellationToken.None));
        Assert.Empty((await ItemsAsync(admin, WorkCenterTab.Tasks)).Items);
    }

    [Fact]
    public async Task Rolled_back_notifications_stay_invisible_and_disabled_recipients_receive_no_new_delivery()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        var (first, second) = await LinkRecipientsAsync(host);
        using var admin = await host.CreateClientAsync("administrator");
        using var other = await host.CreateClientAsync("application-administrator");
        var source = await CreateSourceAsync(admin);
        await host.LinkUserAsync("reader", true, "example.observer", "Observer");
        Guid observer;
        await using (var users = host.Services.CreateAsyncScope())
        {
            observer = Assert.Single(await users.ServiceProvider.GetRequiredService<IPlatformUserRepository>()
                .GetByEmailsAsync(["reader@example.test"])).UserId;
        }
        var hidden = await NotifyAsync(host, Notification(source, [observer], "hidden-source"));
        Assert.Equal(new[] { observer }, hidden.ChangedUserIds);
        using var reader = await host.CreateClientAsync("reader");
        Assert.Empty((await ItemsAsync(reader, WorkCenterTab.Notifications)).Items);
        using var hiddenRead = await reader.PostAsync($"/api/work-center/notifications/{hidden.ItemId}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, hiddenRead.StatusCode);
        var request = Notification(source, [first, second], "transaction-notification");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var transaction = services.GetRequiredService<IUnitOfWork>();
            await transaction.BeginTransactionAsync();
            try
            {
                var created = await services.GetRequiredService<INotificationService>().CreateAsync(request, CancellationToken.None);
                Assert.NotNull(created.ItemId);
                Assert.Equal(2, created.ChangedUserIds.Count);
                Assert.Empty((await ItemsAsync(admin, WorkCenterTab.Notifications)).Items);
            }
            finally
            {
                await transaction.RollbackAsync();
            }
        }
        Assert.Empty((await ItemsAsync(admin, WorkCenterTab.Notifications)).Items);
        Assert.Empty((await ItemsAsync(other, WorkCenterTab.Notifications)).Items);
        var retry = await NotifyAsync(host, request);
        Assert.Equal(2, retry.ChangedUserIds.Count);
        Assert.Single((await ItemsAsync(admin, WorkCenterTab.Notifications)).Items);
        Assert.Single((await ItemsAsync(other, WorkCenterTab.Notifications)).Items);

        await host.DeactivateUserAsync("application-administrator");
        var activeOnly = await NotifyAsync(host, request with { DeduplicationKey = "active-only" });
        Assert.Equal(new[] { first }, activeOnly.ChangedUserIds);
        using var denied = await other.GetAsync("/api/work-center/summary");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedRead = await other.PostAsync($"/api/work-center/notifications/{retry.ItemId}/read", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
    }

    private static InspectionHost CreateHost() => new(configureServices: services =>
        services.AddSingleton<IWorkCenterPreferenceDefinitionSource, Preferences>());

    private static async Task<(Guid First, Guid Second)> LinkRecipientsAsync(InspectionHost host)
    {
        await host.LinkUserAsync("administrator", true, InspectionHost.AdministratorRoleCode, "Inspection administrators");
        await host.LinkUserAsync("application-administrator", true, "example.reviewers", "Inspection reviewers");
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var users = await services.GetRequiredService<IPlatformUserRepository>()
            .GetByEmailsAsync(["administrator@example.test", "application-administrator@example.test"]);
        var first = Assert.Single(users, user => user.Email == "administrator@example.test").UserId;
        var second = Assert.Single(users, user => user.Email == "application-administrator@example.test").UserId;
        var role = await services.GetRequiredService<IPlatformRoleRepository>().GetByCodeAsync(InspectionHost.AdministratorRoleCode);
        await services.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(ct =>
            services.GetRequiredService<IPlatformUserRoleRepository>().ReplaceUserRolesAsync(second, [role!.RoleId], null, ct));
        return (first, second);
    }

    private static async Task<WorkCenterSourceReference> CreateSourceAsync(HttpClient client)
    {
        var document = await PostAsync<DocumentDto>(client, "/api/documents/" + EquipmentDefinitions.InspectionCode, Payload());
        return new WorkCenterSourceReference(NgbResourceKinds.Document, EquipmentDefinitions.InspectionCode,
            document.Id, "Equipment inspection", null);
    }

    private static CreateNotificationRequest Notification(WorkCenterSourceReference source, IReadOnlyList<Guid> users, string key)
        => new(NotificationCode, source, "Inspection ready", "Review the equipment result", null, users,
            null, key, Guid.CreateVersion7(), Guid.CreateVersion7());

    private static async Task<WorkCenterMutationResult> NotifyAsync(InspectionHost host, CreateNotificationRequest request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>().CreateAsync(request, CancellationToken.None);
    }

    private static async Task SetPreferenceAsync(HttpClient client, string code, bool enabled)
    {
        using var response = await client.PutAsJsonAsync("/api/me/notification-preferences",
            new UpdateNotificationPreferencesRequestDto([new(code, NotificationChannel.InApp, enabled)]));
        await EnsureSuccessAsync(response);
    }

    private static async Task<WorkCenterPageDto> ItemsAsync(HttpClient client, WorkCenterTab tab)
        => await client.GetFromJsonAsync<WorkCenterPageDto>("/api/work-center/items?tab=" + tab + "&limit=10", Json)
            ?? throw new InvalidOperationException("Missing Work Center page.");

    private static async Task<WorkCenterSummaryDto> SummaryAsync(HttpClient client)
        => await client.GetFromJsonAsync<WorkCenterSummaryDto>("/api/work-center/summary", Json)
            ?? throw new InvalidOperationException("Missing Work Center summary.");

    private sealed class Preferences : IWorkCenterPreferenceDefinitionSource
    {
        public IReadOnlyList<WorkCenterPreferenceDefinition> GetDefinitions() =>
        [
            new(TaskCode, CoreKind.Task, "Inspection reviews", "Equipment", true, false, CoreSeverity.Information,
                new HashSet<CoreChannel> { CoreChannel.InApp }, null),
            new(NotificationCode, CoreKind.Notification, "Inspection completed", "Equipment", true, true, CoreSeverity.Success,
                new HashSet<CoreChannel> { CoreChannel.InApp }, TimeSpan.FromDays(7)),
            new(MandatoryCode, CoreKind.Notification, "Inspection required", "Equipment", true, false, CoreSeverity.Warning,
                new HashSet<CoreChannel> { CoreChannel.InApp }, null, IsMandatory: true)
        ];
    }
}
