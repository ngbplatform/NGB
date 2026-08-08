using Microsoft.AspNetCore.SignalR;
using Moq;
using NGB.Api.WorkCenter;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.WorkCenter;

public sealed class WorkCenterRealtimeTests
{
    public static TheoryData<PermissionSnapshot?> RejectedSnapshots => new()
    {
        null,
        PermissionSnapshot.Anonymous,
        Snapshot(userId: null, isAuthenticated: true, isActive: true),
        Snapshot(Guid.NewGuid(), isAuthenticated: false, isActive: true),
        Snapshot(Guid.NewGuid(), isAuthenticated: true, isActive: false)
    };

    [Theory]
    [MemberData(nameof(RejectedSnapshots))]
    public async Task Hub_aborts_connections_without_an_active_authenticated_platform_user(
        PermissionSnapshot? snapshot)
    {
        var snapshots = new Mock<IPermissionSnapshotProvider>(MockBehavior.Strict);
        snapshots.Setup(provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot!);
        var context = Context();
        var groups = new Mock<IGroupManager>(MockBehavior.Strict);
        var hub = new WorkCenterHub(snapshots.Object)
        {
            Context = context.Object,
            Groups = groups.Object
        };

        await hub.OnConnectedAsync();

        context.Verify(candidate => candidate.Abort(), Times.Once);
        groups.Verify(candidate => candidate.AddToGroupAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Hub_adds_an_active_authenticated_user_connection_to_its_private_group()
    {
        var userId = Guid.Parse("01990000-0000-7000-8000-000000000101");
        var snapshots = new Mock<IPermissionSnapshotProvider>(MockBehavior.Strict);
        snapshots.Setup(provider => provider.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Snapshot(userId, isAuthenticated: true, isActive: true));
        var context = Context();
        var groups = new Mock<IGroupManager>(MockBehavior.Strict);
        groups.Setup(candidate => candidate.AddToGroupAsync(
                "connection-1",
                WorkCenterHub.GroupName(userId),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hub = new WorkCenterHub(snapshots.Object)
        {
            Context = context.Object,
            Groups = groups.Object
        };

        await hub.OnConnectedAsync();

        context.Verify(candidate => candidate.Abort(), Times.Never);
        groups.VerifyAll();
    }

    [Fact]
    public async Task Realtime_notifier_deduplicates_users_and_ignores_empty_identifiers()
    {
        var first = Guid.Parse("01990000-0000-7000-8000-000000000201");
        var second = Guid.Parse("01990000-0000-7000-8000-000000000202");
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(candidate => candidate.SendCoreAsync(
                "workCenterChanged",
                It.Is<object?[]>(arguments => arguments.Length == 1 && (long)arguments[0]! == 42L),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>(MockBehavior.Strict);
        clients.Setup(candidate => candidate.Groups(It.Is<IReadOnlyList<string>>(groups =>
                groups.SequenceEqual(new[]
                {
                    WorkCenterHub.GroupName(first),
                    WorkCenterHub.GroupName(second)
                }))))
            .Returns(proxy.Object);
        var hub = new Mock<IHubContext<WorkCenterHub>>(MockBehavior.Strict);
        hub.SetupGet(candidate => candidate.Clients).Returns(clients.Object);
        var notifier = new SignalRWorkCenterRealtimeNotifier(hub.Object);

        await notifier.NotifyUsersChangedAsync(42, [Guid.Empty, first, first, second], CancellationToken.None);

        proxy.VerifyAll();
    }

    [Theory]
    [MemberData(nameof(EmptyRecipients))]
    public async Task Realtime_notifier_is_a_noop_when_no_real_user_group_exists(Guid[] userIds)
    {
        var hub = new Mock<IHubContext<WorkCenterHub>>(MockBehavior.Strict);
        var notifier = new SignalRWorkCenterRealtimeNotifier(hub.Object);

        await notifier.NotifyUsersChangedAsync(42, userIds, CancellationToken.None);

        hub.VerifyNoOtherCalls();
    }

    public static TheoryData<Guid[]> EmptyRecipients => new()
    {
        Array.Empty<Guid>(),
        new[] { Guid.Empty, Guid.Empty }
    };

    private static Mock<HubCallerContext> Context()
    {
        var context = new Mock<HubCallerContext>(MockBehavior.Strict);
        context.SetupGet(candidate => candidate.ConnectionId).Returns("connection-1");
        context.SetupGet(candidate => candidate.ConnectionAborted).Returns(CancellationToken.None);
        context.Setup(candidate => candidate.Abort());
        return context;
    }

    private static PermissionSnapshot Snapshot(Guid? userId, bool isAuthenticated, bool isActive)
        => new(
            userId,
            "subject",
            isAuthenticated,
            isActive,
            isBootstrapAdmin: false,
            accessVersion: 1,
            permissions: []);
}
