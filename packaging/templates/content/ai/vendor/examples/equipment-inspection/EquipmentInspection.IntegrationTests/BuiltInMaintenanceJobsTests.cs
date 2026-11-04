using Microsoft.Extensions.DependencyInjection;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.Jobs;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class BuiltInMaintenanceJobsTests
{
    [Fact]
    public async Task Maintenance_jobs_report_healthy_schema_audit_and_accounting_on_an_initialized_application()
    {
        await using var application = new InspectionHost(configureServices: services =>
            services.AddScoped<IJobRunMetrics, ProbeMetrics>());
        await application.StartAsync();

        var jobs = new (Type Implementation, string Id)[]
        {
            (typeof(OperationalRegistersEnsureSchemaJob), PlatformJobCatalog.OperationalRegistersEnsureSchema),
            (typeof(ReferenceRegistersEnsureSchemaJob), PlatformJobCatalog.ReferenceRegistersEnsureSchema),
            (typeof(PlatformSchemaValidateJob), PlatformJobCatalog.PlatformSchemaValidate),
            (typeof(AuditHealthJob), PlatformJobCatalog.AuditHealth),
            (typeof(AccountingIntegrityScanJob), PlatformJobCatalog.AccountingIntegrityScan),
            (typeof(AccountingAggregatesDriftCheckJob), PlatformJobCatalog.AccountingAggregatesDriftCheck),
            (typeof(AccountingOperationsStuckMonitorJob), PlatformJobCatalog.AccountingOperationsStuckMonitor),
        };

        for (var repetition = 0; repetition < 2; repetition++)
        {
            foreach (var (implementation, id) in jobs)
            {
                await using var scope = application.Services.CreateAsyncScope();
                var job = (IPlatformBackgroundJob)ActivatorUtilities.CreateInstance(scope.ServiceProvider, implementation);
                Assert.Equal(id, job.JobId);
                await job.RunAsync(CancellationToken.None);
                var counters = scope.ServiceProvider.GetRequiredService<IJobRunMetrics>().Snapshot();
                switch (id)
                {
                    case PlatformJobCatalog.OperationalRegistersEnsureSchema:
                    case PlatformJobCatalog.ReferenceRegistersEnsureSchema:
                        Assert.True(counters["registers_total"] > 0);
                        Assert.Equal(counters["registers_total"], counters["registers_ok"]);
                        Assert.Equal(0, counters["registers_failed"]);
                        Assert.Equal(0, counters["has_failures"]);
                        break;
                    case PlatformJobCatalog.PlatformSchemaValidate:
                        Assert.Equal(6, counters["schemas_total"]);
                        Assert.Equal(counters["schemas_total"], counters["schemas_validated"]);
                        break;
                    case PlatformJobCatalog.AuditHealth:
                        Assert.Equal(1, counters["health_ok"]);
                        Assert.Equal(0, counters["audit.missing_triggers"]);
                        Assert.Equal(0, counters["audit.orphan_changes"]);
                        break;
                    case PlatformJobCatalog.AccountingIntegrityScan:
                        Assert.Equal(2, counters["periods_scanned"]);
                        break;
                    case PlatformJobCatalog.AccountingAggregatesDriftCheck:
                        Assert.Equal(2, counters["periods_checked"]);
                        Assert.Equal(0, counters["diff_total"]);
                        Assert.Equal(0, counters["drift_detected"]);
                        break;
                    case PlatformJobCatalog.AccountingOperationsStuckMonitor:
                        Assert.Equal(25, counters["page_size"]);
                        Assert.Equal(30, counters["lookback_days"]);
                        Assert.Equal(0, counters["stale_count"]);
                        Assert.Equal(0, counters["problem"]);
                        break;
                }
            }
        }
    }

    private sealed class ProbeMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            _counters[name] = _counters.GetValueOrDefault(name) + delta;
        }

        public void Set(string name, long value)
        {
            _counters[name] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot()
        {
            return new Dictionary<string, long>(_counters);
        }
    }
}
