using FluentAssertions;
using Moq;
using NGB.Persistence.Outbox;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.Tests.Observability;
using NGB.Runtime.WorkCenter;
using Xunit;

namespace NGB.Runtime.Tests.WorkCenter;

[Collection(TelemetrySerialCollection.Name)]
public sealed class WorkCenterOperationalHealthReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reader_combines_outbox_and_task_health_and_calculates_oldest_pending_age()
    {
        var (reader, outbox, workCenter) = Reader(Now.AddMinutes(-7).UtcDateTime);

        var result = await reader.ReadAsync(CancellationToken.None);

        result.Should().Be(new NGB.Application.Abstractions.Services.WorkCenterOperationalHealthSnapshot(
            PendingDeliveryCount: 5,
            FailedDeliveryCount: 2,
            OldestPendingAgeSeconds: 420,
            OpenTaskCount: 11,
            OverdueTaskCount: 3));
        outbox.VerifyAll();
        workCenter.VerifyAll();
    }

    [Fact]
    public async Task Reader_reports_zero_age_when_there_is_no_pending_event()
    {
        var (reader, _, _) = Reader(oldest: null);

        var result = await reader.ReadAsync(CancellationToken.None);

        result.OldestPendingAgeSeconds.Should().Be(0);
    }

    [Fact]
    public async Task Reader_clamps_future_outbox_timestamps_to_zero_age()
    {
        var (reader, _, _) = Reader(Now.AddMinutes(1).UtcDateTime);

        var result = await reader.ReadAsync(CancellationToken.None);

        result.OldestPendingAgeSeconds.Should().Be(0);
    }

    private static (
        WorkCenterOperationalHealthReader Reader,
        Mock<IOutboxEventRepository> Outbox,
        Mock<IWorkCenterReadRepository> WorkCenter) Reader(DateTime? oldest)
    {
        var outbox = new Mock<IOutboxEventRepository>(MockBehavior.Strict);
        outbox.Setup(repository => repository.GetHealthAsync("work-center", CancellationToken.None))
            .ReturnsAsync((5L, oldest, 2L));
        var workCenter = new Mock<IWorkCenterReadRepository>(MockBehavior.Strict);
        workCenter.Setup(repository => repository.GetTaskHealthAsync(Now.UtcDateTime, CancellationToken.None))
            .ReturnsAsync(new WorkCenterTaskHealthRecord(11, 3));

        return (
            new WorkCenterOperationalHealthReader(outbox.Object, workCenter.Object, new FixedTimeProvider(Now)),
            outbox,
            workCenter);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
