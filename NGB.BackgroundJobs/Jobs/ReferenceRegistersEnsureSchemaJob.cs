using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Jobs.Internal;
using NGB.Runtime.ReferenceRegisters;

namespace NGB.BackgroundJobs.Jobs;

/// <summary>
/// Nightly: ensures physical schema for all Reference Registers.
///
/// The job reads back a health report and fails if any register remains unhealthy.
/// </summary>
public sealed class ReferenceRegistersEnsureSchemaJob(
    IReferenceRegisterAdminMaintenanceService maintenance,
    ILogger<ReferenceRegistersEnsureSchemaJob> logger,
    IJobRunMetrics metrics,
    TimeProvider? timeProvider = null)
    : IPlatformBackgroundJob
{
    public string JobId => "refreg.ensure_schema";

    public Task RunAsync(CancellationToken cancellationToken) =>
        EnsureSchemaJobRunner.RunAsync(
            logger,
            metrics,
            JobId,
            "Reference registers",
            timeProvider ?? TimeProvider.System,
            async ct =>
            {
                var report = await maintenance.EnsurePhysicalSchemaForAllAsync(ct);
                return (report.TotalCount, report.OkCount);
            },
            cancellationToken);
}
