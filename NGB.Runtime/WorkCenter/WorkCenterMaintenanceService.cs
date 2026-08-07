using NGB.Application.Abstractions.Services;
using Microsoft.Extensions.Options;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.WorkCenter;
using NGB.Runtime.UnitOfWork;

namespace NGB.Runtime.WorkCenter;

internal sealed class WorkCenterMaintenanceService(
    IUnitOfWork uow,
    IWorkCenterMaintenanceRepository repository,
    TimeProvider timeProvider,
    IOptions<NgbWorkCenterOptions> options)
    : IWorkCenterMaintenanceService
{
    public async Task<int> PruneAsync(CancellationToken ct)
    {
        var policy = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoffs = new WorkCenterRetentionCutoffs(
            now.Subtract(policy.DocumentActionExecutionRetention),
            now.Subtract(policy.TerminalTaskRetention),
            now.Subtract(policy.NotificationDeliveryRetention),
            now.Subtract(policy.OutboxRetention));

        var total = 0;
        for (var batch = 0; batch < policy.MaximumMaintenanceBatchesPerRun; batch++)
        {
            var result = await uow.ExecuteInUowTransactionAsync(
                innerCt => repository.PruneAsync(cutoffs, policy.MaintenanceBatchSize, innerCt),
                ct);

            total += result.Total;

            if (result.Total == 0)
                break;
        }

        return total;
    }
}
