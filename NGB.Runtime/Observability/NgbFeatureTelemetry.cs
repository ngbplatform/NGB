using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NGB.Runtime.Observability;

public static class NgbFeatureTelemetry
{
    internal const string MeterName = "NGB.Platform";
    internal const string ActivitySourceName = "NGB.Platform.DocumentActionsWorkCenter";

    internal static readonly ActivitySource Activities = new(ActivitySourceName, "3.0.0");
    private static readonly Meter Meter = new(MeterName, "3.0.0");

    internal static readonly Counter<long> DocumentActionExecutions =
        Meter.CreateCounter<long>("ngb.document_action.executions");

    internal static readonly Counter<long> DocumentActionFailures =
        Meter.CreateCounter<long>("ngb.document_action.failures");

    internal static readonly Counter<long> DocumentActionConcurrencyConflicts =
        Meter.CreateCounter<long>("ngb.document_action.concurrency_conflicts");

    internal static readonly Histogram<double> DocumentActionDuration =
        Meter.CreateHistogram<double>("ngb.document_action.duration", "ms");

    private static long _outboxPending;
    private static double _outboxOldestAgeSeconds;
    private static long _workCenterTasksOpen;
    private static long _workCenterTasksOverdue;

    private static readonly ObservableGauge<long> OutboxPending =
        Meter.CreateObservableGauge("ngb.outbox.pending", () => Volatile.Read(ref _outboxPending));

    private static readonly ObservableGauge<double> OutboxOldestAge =
        Meter.CreateObservableGauge("ngb.outbox.oldest_age", () => Volatile.Read(ref _outboxOldestAgeSeconds), "s");

    internal static readonly Counter<long> OutboxFailures = Meter.CreateCounter<long>("ngb.outbox.failures");
    internal static readonly Counter<long> OutboxProcessed = Meter.CreateCounter<long>("ngb.outbox.processed");

    private static readonly ObservableGauge<long> WorkCenterTasksOpen =
        Meter.CreateObservableGauge("ngb.work_center.tasks_open", () => Volatile.Read(ref _workCenterTasksOpen));

    private static readonly ObservableGauge<long> WorkCenterTasksOverdue =
        Meter.CreateObservableGauge("ngb.work_center.tasks_overdue", () => Volatile.Read(ref _workCenterTasksOverdue));
   
    internal static readonly Counter<long> WorkCenterNotificationsCreated =
        Meter.CreateCounter<long>("ngb.work_center.notifications_created");
    
    internal static readonly Histogram<double> WorkCenterPolicyDuration =
        Meter.CreateHistogram<double>("ngb.work_center.policy_duration", "ms");

    internal static readonly Histogram<double> WorkCenterQueryDuration =
        Meter.CreateHistogram<double>("ngb.work_center.query_duration", "ms");

    public static void ObserveOperationalHealth(
        long outboxPending,
        double outboxOldestAgeSeconds,
        long workCenterTasksOpen,
        long workCenterTasksOverdue)
    {
        Volatile.Write(ref _outboxPending, Math.Max(0, outboxPending));
        Volatile.Write(ref _outboxOldestAgeSeconds, Math.Max(0, outboxOldestAgeSeconds));
        Volatile.Write(ref _workCenterTasksOpen, Math.Max(0, workCenterTasksOpen));
        Volatile.Write(ref _workCenterTasksOverdue, Math.Max(0, workCenterTasksOverdue));
    }
}
