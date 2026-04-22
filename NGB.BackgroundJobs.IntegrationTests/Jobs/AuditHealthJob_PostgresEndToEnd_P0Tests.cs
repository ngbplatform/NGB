using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

[Collection(HangfirePostgresCollection.Name)]
public sealed class AuditHealthJob_PostgresEndToEnd_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_WhenTriggersPresentAndNoOrphans_SetsHealthOkTo1()
    {
        await using var uow = new PostgresUnitOfWork(fixture.ConnectionString, NullLogger<PostgresUnitOfWork>.Instance);
        var metrics = new TestJobRunMetrics();

        var job = new AuditHealthJob(uow, NullLogger<AuditHealthJob>.Instance, metrics);

        await job.RunAsync(CancellationToken.None);

        var snapshot = metrics.Snapshot();
        snapshot.Should().ContainKey("health_ok");
        snapshot["health_ok"].Should().Be(1);
        snapshot["audit.missing_triggers"].Should().Be(0);
        snapshot["audit.orphan_changes"].Should().Be(0);
        snapshot.Should().ContainKey("audit.events_count");
    }

    [Fact]
    public async Task RunAsync_WhenEventsTriggerMissing_Throws()
    {
        await using var uow = new PostgresUnitOfWork(fixture.ConnectionString, NullLogger<PostgresUnitOfWork>.Instance);
        var metrics = new TestJobRunMetrics();
        var job = new AuditHealthJob(uow, NullLogger<AuditHealthJob>.Instance, metrics);

        await uow.BeginTransactionAsync();
        try
        {
            // Transactional DDL: we can roll back to restore the trigger for other tests.
            const string dropSql = "DROP TRIGGER IF EXISTS trg_platform_audit_events_append_only ON public.platform_audit_events;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(dropSql, transaction: uow.Transaction));

            var act = async () => await job.RunAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("*triggers are missing*");

            metrics.Snapshot()["audit.events_trigger_present"].Should().Be(0);
        }
        finally
        {
            await uow.RollbackAsync();
        }
    }

    [Fact]
    public async Task RunAsync_WhenOrphanChangesExist_Throws()
    {
        await using var uow = new PostgresUnitOfWork(fixture.ConnectionString, NullLogger<PostgresUnitOfWork>.Instance);
        var metrics = new TestJobRunMetrics();
        var job = new AuditHealthJob(uow, NullLogger<AuditHealthJob>.Instance, metrics);

        await uow.BeginTransactionAsync();
        try
        {
            // Create an orphan change row by temporarily removing the FK inside the transaction.
            const string dropFkSql = "ALTER TABLE public.platform_audit_event_changes DROP CONSTRAINT IF EXISTS fk_platform_audit_event_changes_event;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(dropFkSql, transaction: uow.Transaction));

            const string insertOrphanSql = """
                                     INSERT INTO public.platform_audit_event_changes (
                                         audit_change_id,
                                         audit_event_id,
                                         ordinal,
                                         field_path,
                                         old_value_jsonb,
                                         new_value_jsonb
                                     ) VALUES (
                                         @AuditChangeId,
                                         @AuditEventId,
                                         1,
                                         'test',
                                         NULL,
                                         NULL
                                     );
                                     """;

            await uow.Connection.ExecuteAsync(
                new CommandDefinition(
                    insertOrphanSql,
                    new { AuditChangeId = Guid.CreateVersion7(), AuditEventId = Guid.CreateVersion7() },
                    transaction: uow.Transaction));

            var act = async () => await job.RunAsync(CancellationToken.None);
            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("*orphan change rows*");

            metrics.Snapshot()["audit.orphan_changes"].Should().Be(1);
        }
        finally
        {
            await uow.RollbackAsync();
        }
    }

    private sealed class TestJobRunMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (delta == 0)
                return;

            name = name.Trim();
            _counters.TryGetValue(name, out var existing);
            _counters[name] = existing + delta;
        }

        public void Set(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _counters[name.Trim()] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counters);
    }
}
