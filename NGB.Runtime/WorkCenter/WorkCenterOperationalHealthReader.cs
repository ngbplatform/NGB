using NGB.Application.Abstractions.Services;
using NGB.Persistence.Outbox;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.Observability;

namespace NGB.Runtime.WorkCenter;

internal sealed class WorkCenterOperationalHealthReader(
    IOutboxEventRepository outbox,
    IWorkCenterReadRepository workCenter,
    TimeProvider timeProvider)
    : IWorkCenterOperationalHealthReader
{
    private const string ConsumerCode = "work-center";

    public async Task<WorkCenterOperationalHealthSnapshot> ReadAsync(CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var (pending, oldest, failed) = await outbox.GetHealthAsync(ConsumerCode, ct);
        var taskHealth = await workCenter.GetTaskHealthAsync(nowUtc, ct);
        var ageSeconds = oldest is null
            ? 0d
            : Math.Max(0d, (nowUtc - oldest.Value).TotalSeconds);

        NgbFeatureTelemetry.ObserveOperationalHealth(
            pending,
            ageSeconds,
            taskHealth.OpenTaskCount,
            taskHealth.OverdueTaskCount);

        return new WorkCenterOperationalHealthSnapshot(
            pending,
            failed,
            ageSeconds,
            taskHealth.OpenTaskCount,
            taskHealth.OverdueTaskCount);
    }
}
