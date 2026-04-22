using Microsoft.Extensions.Logging;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Jobs.Internal;
using NGB.Runtime.OperationalRegisters;

namespace NGB.BackgroundJobs.Jobs;

/// <summary>
/// Nightly: ensures physical schema for all Operational Registers.
///
/// DDL is serialized by the Operational Registers schema lock inside the Postgres stores.
/// The job reads back a health report and fails if any register remains unhealthy.
/// </summary>
public sealed class OperationalRegistersEnsureSchemaJob(
    IOperationalRegisterAdminMaintenanceService maintenance,
    ILogger<OperationalRegistersEnsureSchemaJob> logger,
    IJobRunMetrics metrics,
    TimeProvider? timeProvider = null)
    : IPlatformBackgroundJob
{
    public string JobId => "opreg.ensure_schema";

    public Task RunAsync(CancellationToken cancellationToken) =>
        EnsureSchemaJobRunner.RunAsync(
            logger,
            metrics,
            JobId,
            "Operational registers",
            timeProvider ?? TimeProvider.System,
            async ct =>
            {
                var report = await maintenance.EnsurePhysicalSchemaForAllAsync(ct);
                return (report.TotalCount, report.OkCount);
            },
            cancellationToken);
}
