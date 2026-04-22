using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Jobs;
using NGB.BackgroundJobs.Observability;
using NGB.OperationalRegisters.Contracts;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests;

public sealed class EnsureSchemaJobs_UnhealthyReport_ThrowsAndSetsMetrics_P0Tests
{
    [Fact]
    public async Task ReferenceRegistersEnsureSchemaJob_WhenReportHasFailures_ThrowsAndSetsCounters()
    {
        var nowUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var ok = new ReferenceRegisterPhysicalSchemaHealth(
            Register: new ReferenceRegisterAdminItem(
                RegisterId: Guid.CreateVersion7(),
                Code: "RR_OK",
                CodeNorm: "rr_ok",
                TableCode: "rr_ok",
                Name: "ok",
                Periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                RecordMode: ReferenceRegisterRecordMode.Independent,
                HasRecords: false,
                CreatedAtUtc: nowUtc,
                UpdatedAtUtc: nowUtc),
            Records: new ReferenceRegisterPhysicalTableHealth(
                TableName: "refreg_rr_ok__records",
                Exists: true,
                MissingColumns: Array.Empty<string>(),
                MissingIndexes: Array.Empty<string>(),
                HasAppendOnlyGuard: true));

        var bad = new ReferenceRegisterPhysicalSchemaHealth(
            Register: new ReferenceRegisterAdminItem(
                RegisterId: Guid.CreateVersion7(),
                Code: "RR_BAD",
                CodeNorm: "rr_bad",
                TableCode: "rr_bad",
                Name: "bad",
                Periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                RecordMode: ReferenceRegisterRecordMode.Independent,
                HasRecords: true,
                CreatedAtUtc: nowUtc,
                UpdatedAtUtc: nowUtc),
            Records: new ReferenceRegisterPhysicalTableHealth(
                TableName: "refreg_rr_bad__records",
                Exists: true,
                MissingColumns: Array.Empty<string>(),
                MissingIndexes: new[] { "index(key_hash, dimension_set_id, period_bucket_utc)" },
                HasAppendOnlyGuard: false));

        var report = new ReferenceRegisterPhysicalSchemaHealthReport(new[] { ok, bad });

        var metrics = new JobRunMetrics();
        var maintenance = new FakeReferenceRegisterAdminMaintenanceService(report);

        var job = new ReferenceRegistersEnsureSchemaJob(
            maintenance,
            NullLogger<ReferenceRegistersEnsureSchemaJob>.Instance,
            metrics);

        var act = async () => await job.RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Reference registers schema is unhealthy after ensure*");

        var snapshot = metrics.Snapshot();
        snapshot["registers_total"].Should().Be(2);
        snapshot["registers_ok"].Should().Be(1);
        snapshot["registers_failed"].Should().Be(1);
        snapshot["has_failures"].Should().Be(1);
    }

    [Fact]
    public async Task OperationalRegistersEnsureSchemaJob_WhenReportHasFailures_ThrowsAndSetsCounters()
    {
        var nowUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        static OperationalRegisterPhysicalTableHealth OkTable(string name)
            => new(
                TableName: name,
                Exists: true,
                MissingColumns: Array.Empty<string>(),
                MissingIndexes: Array.Empty<string>(),
                HasAppendOnlyGuard: true);

        static OperationalRegisterPhysicalTableHealth BadTable(string name)
            => new(
                TableName: name,
                Exists: true,
                MissingColumns: Array.Empty<string>(),
                MissingIndexes: new[] { "index(document_id)" },
                HasAppendOnlyGuard: false);

        var ok = new OperationalRegisterPhysicalSchemaHealth(
            Register: new OperationalRegisterAdminItem(
                RegisterId: Guid.CreateVersion7(),
                Code: "OR_OK",
                CodeNorm: "or_ok",
                TableCode: "or_ok",
                Name: "ok",
                HasMovements: false,
                CreatedAtUtc: nowUtc,
                UpdatedAtUtc: nowUtc),
            Movements: OkTable("opreg_or_ok__movements"),
            Turnovers: OkTable("opreg_or_ok__turnovers"),
            Balances: OkTable("opreg_or_ok__balances"));

        var bad = new OperationalRegisterPhysicalSchemaHealth(
            Register: new OperationalRegisterAdminItem(
                RegisterId: Guid.CreateVersion7(),
                Code: "OR_BAD",
                CodeNorm: "or_bad",
                TableCode: "or_bad",
                Name: "bad",
                HasMovements: true,
                CreatedAtUtc: nowUtc,
                UpdatedAtUtc: nowUtc),
            Movements: BadTable("opreg_or_bad__movements"),
            Turnovers: OkTable("opreg_or_bad__turnovers"),
            Balances: OkTable("opreg_or_bad__balances"));

        var report = new OperationalRegisterPhysicalSchemaHealthReport(new[] { ok, bad });

        var metrics = new JobRunMetrics();
        var maintenance = new FakeOperationalRegisterAdminMaintenanceService(report);

        var job = new OperationalRegistersEnsureSchemaJob(
            maintenance,
            NullLogger<OperationalRegistersEnsureSchemaJob>.Instance,
            metrics);

        var act = async () => await job.RunAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*Operational registers schema is unhealthy after ensure*");

        var snapshot = metrics.Snapshot();
        snapshot["registers_total"].Should().Be(2);
        snapshot["registers_ok"].Should().Be(1);
        snapshot["registers_failed"].Should().Be(1);
        snapshot["has_failures"].Should().Be(1);
    }

    private sealed class FakeReferenceRegisterAdminMaintenanceService(ReferenceRegisterPhysicalSchemaHealthReport report)
        : IReferenceRegisterAdminMaintenanceService
    {
        public Task<ReferenceRegisterPhysicalSchemaHealth?> EnsurePhysicalSchemaByIdAsync(Guid registerId, CancellationToken ct = default)
            => Task.FromResult<ReferenceRegisterPhysicalSchemaHealth?>(null);

        public Task<ReferenceRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(CancellationToken ct = default)
            => Task.FromResult(report);
    }

    private sealed class FakeOperationalRegisterAdminMaintenanceService(OperationalRegisterPhysicalSchemaHealthReport report)
        : IOperationalRegisterAdminMaintenanceService
    {
        public Task<OperationalRegisterPhysicalSchemaHealth?> EnsurePhysicalSchemaByIdAsync(Guid registerId, CancellationToken ct = default)
            => Task.FromResult<OperationalRegisterPhysicalSchemaHealth?>(null);

        public Task<OperationalRegisterPhysicalSchemaHealthReport> EnsurePhysicalSchemaForAllAsync(CancellationToken ct = default)
            => Task.FromResult(report);

        public Task MarkFinalizationDirtyAsync(Guid registerId, DateOnly periodMonth, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> FinalizeDirtyAsync(int maxItems = 50, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> FinalizeRegisterDirtyAsync(Guid registerId, int maxPeriods = 50, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
