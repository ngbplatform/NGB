using NGB.Tools.Exceptions;

namespace NGB.Core.WorkCenter;

public sealed class WorkCenterTaskClaimConflictException(Guid taskId, long expectedVersion)
    : NgbConflictException(
        "The task was already claimed or changed.",
        "work_center.task_claim_conflict",
        new Dictionary<string, object?>
        {
            ["taskId"] = taskId,
            ["expectedVersion"] = expectedVersion
        });
