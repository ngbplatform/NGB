using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class IndependentReferenceRegisterTests
{
    [Fact]
    public async Task Periodic_versions_respect_effective_time_recorded_time_and_tombstones()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        var key = await CreateEquipmentKeyAsync(host, "Periodic rate bench");
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var register = await ConfigureAsync(services, "example.hourly-rate", ReferenceRegisterPeriodicity.Month);
        var writes = services.GetRequiredService<IReferenceRegisterIndependentWriteService>();
        var reads = services.GetRequiredService<IReferenceRegisterReadService>();
        var now = DateTime.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var previous = month.AddMonths(-1).AddDays(10).AddHours(9);
        var next = month.AddMonths(1);
        var command = Guid.CreateVersion7();

        Assert.Equal(ReferenceRegisterWriteResult.Executed,
            await writes.UpsertAsync(register, key, previous, Values(10m), command));
        var original = await reads.SliceLastAsync(register, key, DateTime.UtcNow);
        Assert.NotNull(original);
        Assert.Equal(month.AddMonths(-1), original.PeriodBucketUtc);
        Assert.Equal(previous, original.PeriodUtc);
        Assert.Null(original.RecorderDocumentId);
        Assert.Null(await reads.SliceLastAsync(register, key, original.RecordedAtUtc.AddTicks(-10)));
        Assert.Equal(ReferenceRegisterWriteResult.AlreadyCompleted,
            await writes.UpsertAsync(register, key, previous, Values(10m), command));
        Assert.Single(await reads.GetKeyHistoryAsync(register, key, DateTime.UtcNow, periodUtc: previous));

        await writes.UpsertByDimensionSetIdAsync(register, original.DimensionSetId, previous, Values(15m), Guid.CreateVersion7());
        await writes.UpsertAsync(register, key, next.AddDays(10), Values(20m), Guid.CreateVersion7());
        await writes.UpsertAsync(register, key, next.AddDays(20), Values(25m), Guid.CreateVersion7());
        Assert.Equal(15m, (await reads.SliceLastAsync(register, key, DateTime.UtcNow))!.Values["amount"]);
        Assert.Equal(original.RecordId, (await reads.SliceLastAsync(register, key, original.RecordedAtUtc))!.RecordId);
        Assert.Equal(15m, (await reads.SliceLastAsync(register, key, next.AddDays(5)))!.Values["amount"]);
        Assert.Equal(20m, (await reads.SliceLastAsync(register, key, next.AddDays(15)))!.Values["amount"]);
        Assert.Equal(25m, (await reads.SliceLastByDimensionSetIdAsync(register, original.DimensionSetId, next.AddDays(25)))!.Values["amount"]);
        var bucket = await reads.GetKeyHistoryAsync(register, key, next.AddDays(25), periodUtc: next.AddDays(26));
        Assert.Equal(new[] { 25m, 20m }, bucket.Select(row => (decimal)row.Values["amount"]!));

        var tombstoneCommand = Guid.CreateVersion7();
        Assert.Equal(ReferenceRegisterWriteResult.Executed,
            await writes.TombstoneAsync(register, key, next.AddDays(15), tombstoneCommand));
        Assert.Equal(ReferenceRegisterWriteResult.AlreadyCompleted,
            await writes.TombstoneAsync(register, key, next.AddDays(15), tombstoneCommand));
        Assert.Null(await reads.SliceLastAsync(register, key, next.AddDays(15)));
        var tombstone = await reads.SliceLastAsync(register, key, next.AddDays(15), includeDeleted: true);
        Assert.True(tombstone!.IsDeleted);
        Assert.Equal(20m, tombstone.Values["amount"]);
        Assert.Equal(next.AddDays(10), tombstone.PeriodUtc);
        Assert.Equal(25m, (await reads.SliceLastAsync(register, key, next.AddDays(25)))!.Values["amount"]);

        await writes.TombstoneByDimensionSetIdAsync(register, original.DimensionSetId, next.AddDays(15), Guid.CreateVersion7());
        Assert.Equal(3, (await reads.GetKeyHistoryAsync(register, key, next.AddDays(25), periodUtc: next, includeDeleted: true)).Count);
        await writes.UpsertAsync(register, key, next.AddDays(10), Values(22m), Guid.CreateVersion7());
        Assert.Equal(22m, (await reads.SliceLastAsync(register, key, next.AddDays(15)))!.Values["amount"]);
        var previousHistory = await ReadHistoryAsync(reads, register, original.DimensionSetId, next.AddDays(25), previous);
        var nextHistory = await ReadHistoryAsync(reads, register, original.DimensionSetId, next.AddDays(25), next);
        var history = previousHistory.Concat(nextHistory).OrderByDescending(row => row.RecordId).ToList();
        Assert.Equal(6, history.Count);
        Assert.Equal(original.RecordId, history[^1].RecordId);
        Assert.Single(history, row => row.IsDeleted);
        var audit = await services.GetRequiredService<IAuditEventReader>().QueryAsync(
            new AuditLogQuery(EntityKind: AuditEntityKind.ReferenceRegister, EntityId: register));
        Assert.Equal(5, audit.Count(row => row.ActionCode == "refreg.records.upsert"));
        Assert.Single(audit, row => row.ActionCode == "refreg.records.tombstone");
    }

    [Fact]
    public async Task Every_periodicity_normalizes_utc_buckets_and_rejects_incompatible_input()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        var key = await CreateEquipmentKeyAsync(host, "Bucket bench");
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var writes = services.GetRequiredService<IReferenceRegisterIndependentWriteService>();
        var reads = services.GetRequiredService<IReferenceRegisterReadService>();
        var instant = new DateTime(2025, 5, 19, 16, 23, 47, DateTimeKind.Utc).AddMilliseconds(125);
        var subordinate = await Assert.ThrowsAsync<ReferenceRegisterRecordsValidationException>(() =>
            writes.UpsertAsync(ReferenceRegisterId.FromCode(EquipmentConditionDefinitions.RegisterCode),
                key, null, Values(1m), Guid.CreateVersion7()));
        Assert.Equal("record_mode_not_independent", subordinate.Reason);
        foreach (var (periodicity, expected) in new (ReferenceRegisterPeriodicity, DateTime?)[]
        {
            (ReferenceRegisterPeriodicity.NonPeriodic, null),
            (ReferenceRegisterPeriodicity.Second, new DateTime(2025, 5, 19, 16, 23, 47, DateTimeKind.Utc)),
            (ReferenceRegisterPeriodicity.Day, new DateTime(2025, 5, 19, 0, 0, 0, DateTimeKind.Utc)),
            (ReferenceRegisterPeriodicity.Month, new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
            (ReferenceRegisterPeriodicity.Quarter, new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
            (ReferenceRegisterPeriodicity.Year, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        })
        {
            var register = await ConfigureAsync(services, "example.bucket-" + periodicity.ToString().ToLowerInvariant(), periodicity);
            DateTime? period = periodicity == ReferenceRegisterPeriodicity.NonPeriodic ? null : instant;
            var command = Guid.CreateVersion7();
            var invalid = await Assert.ThrowsAsync<ReferenceRegisterRecordsValidationException>(() =>
                writes.UpsertAsync(register, key, period is null ? instant : null, Values(8.50m), command));
            Assert.Equal(period is null ? "period_not_allowed_for_non_periodic" : "period_required_for_periodic", invalid.Reason);
            Assert.Equal(ReferenceRegisterWriteResult.Executed,
                await writes.UpsertAsync(register, key, period, Values(8.50m), command));
            var record = await reads.SliceLastAsync(register, key, DateTime.UtcNow);
            Assert.NotNull(record);
            Assert.Equal(period, record.PeriodUtc);
            Assert.Equal(expected, record.PeriodBucketUtc);
            Assert.Equal(8.50m, record.Values["amount"]);
            Assert.Single(await reads.GetKeyHistoryAsync(register, key, DateTime.UtcNow, periodUtc: period));

            await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
                writes.UpsertAsync(register, key, DateTime.SpecifyKind(instant, DateTimeKind.Unspecified), Values(1m), Guid.CreateVersion7()));
            var recorder = await Assert.ThrowsAsync<ReferenceRegisterRecordsValidationException>(() =>
                reads.SliceLastAsync(register, key, DateTime.UtcNow, recorderDocumentId: Guid.CreateVersion7()));
            Assert.Equal("recorder_forbidden", recorder.Reason);
            var missingDimension = await Assert.ThrowsAsync<ReferenceRegisterRecordsValidationException>(() =>
                writes.UpsertAsync(register, [], period, Values(1m), Guid.CreateVersion7()));
            Assert.Equal("missing_required_dimensions", missingDimension.Reason);
            Assert.Single(await reads.GetKeyHistoryAsync(register, key, DateTime.UtcNow, periodUtc: period));
        }
    }

    [Fact]
    public async Task Outer_transaction_rolls_back_records_audit_and_command_completion_together()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        var key = await CreateEquipmentKeyAsync(host, "Transactional bench");
        Guid register;
        var command = Guid.CreateVersion7();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            register = await ConfigureAsync(services, "example.transaction-rate", ReferenceRegisterPeriodicity.NonPeriodic);
            var writes = services.GetRequiredService<IReferenceRegisterIndependentWriteService>();
            var reads = services.GetRequiredService<IReferenceRegisterReadService>();
            var audit = services.GetRequiredService<IAuditEventReader>();
            var uow = services.GetRequiredService<IUnitOfWork>();
            await Assert.ThrowsAsync<NgbInvariantViolationException>(() =>
                writes.UpsertAsync(register, key, null, Values(12m), command, manageTransaction: false));
            await uow.BeginTransactionAsync();
            try
            {
                await writes.UpsertAsync(register, key, null, Values(12m), command, manageTransaction: false);
                Assert.Equal(12m, (await reads.SliceLastAsync(register, key, DateTime.UtcNow))!.Values["amount"]);
                Assert.Single(await audit.QueryAsync(new AuditLogQuery(EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: register, ActionCode: "refreg.records.upsert")));
                await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
                    writes.UpsertAsync(register, key, null, Values(13m), Guid.CreateVersion7()));
                Assert.True(uow.HasActiveTransaction);
            }
            finally
            {
                await uow.RollbackAsync();
            }
        }

        await using var verification = host.Services.CreateAsyncScope();
        var provider = verification.ServiceProvider;
        var committed = provider.GetRequiredService<IReferenceRegisterReadService>();
        var events = provider.GetRequiredService<IAuditEventReader>();
        Assert.Empty(await committed.GetKeyHistoryAsync(register, key, DateTime.UtcNow, includeDeleted: true));
        Assert.Empty(await events.QueryAsync(new AuditLogQuery(EntityKind: AuditEntityKind.ReferenceRegister,
            EntityId: register, ActionCode: "refreg.records.upsert")));
        var retry = provider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
        Assert.Equal(ReferenceRegisterWriteResult.Executed,
            await retry.UpsertAsync(register, key, null, Values(12m), command));
        Assert.Equal(ReferenceRegisterWriteResult.AlreadyCompleted,
            await retry.UpsertAsync(register, key, null, Values(12m), command));
        Assert.Single(await committed.GetKeyHistoryAsync(register, key, DateTime.UtcNow));
        Assert.Single(await events.QueryAsync(new AuditLogQuery(EntityKind: AuditEntityKind.ReferenceRegister,
            EntityId: register, ActionCode: "refreg.records.upsert")));

        var tombstoneCommand = Guid.CreateVersion7();
        var unitOfWork = provider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginTransactionAsync();
        try
        {
            await retry.TombstoneAsync(register, key, DateTime.UtcNow, tombstoneCommand, manageTransaction: false);
            Assert.Null(await committed.SliceLastAsync(register, key, DateTime.UtcNow));
            Assert.Single(await events.QueryAsync(new AuditLogQuery(EntityKind: AuditEntityKind.ReferenceRegister,
                EntityId: register, ActionCode: "refreg.records.tombstone")));
        }
        finally
        {
            await unitOfWork.RollbackAsync();
        }
        Assert.Equal(12m, (await committed.SliceLastAsync(register, key, DateTime.UtcNow))!.Values["amount"]);
        Assert.Single(await committed.GetKeyHistoryAsync(register, key, DateTime.UtcNow, includeDeleted: true));
        Assert.Empty(await events.QueryAsync(new AuditLogQuery(EntityKind: AuditEntityKind.ReferenceRegister,
            EntityId: register, ActionCode: "refreg.records.tombstone")));
        Assert.Equal(ReferenceRegisterWriteResult.Executed,
            await retry.TombstoneAsync(register, key, DateTime.UtcNow, tombstoneCommand));
        Assert.Equal(ReferenceRegisterWriteResult.AlreadyCompleted,
            await retry.TombstoneAsync(register, key, DateTime.UtcNow, tombstoneCommand));
        Assert.Null(await committed.SliceLastAsync(register, key, DateTime.UtcNow));
        Assert.Equal(2, (await committed.GetKeyHistoryAsync(register, key, DateTime.UtcNow, includeDeleted: true)).Count);
    }

    [Fact]
    public async Task Concurrent_commands_commit_once_and_visible_paging_skips_deleted_equipment()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        var keys = new List<DimensionValue[]>();
        for (var index = 0; index < 4; index++)
            keys.Add(await CreateEquipmentKeyAsync(host, "Paged rate bench " + index));

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var register = await ConfigureAsync(services, "example.paged-rate", ReferenceRegisterPeriodicity.NonPeriodic);
        var command = Guid.CreateVersion7();
        async Task<ReferenceRegisterWriteResult> WriteAsync()
        {
            await using var concurrent = host.Services.CreateAsyncScope();
            return await concurrent.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>()
                .UpsertAsync(register, keys[0], null, Values(7m), command);
        }
        var results = await Task.WhenAll(WriteAsync(), WriteAsync());
        Assert.Single(results, result => result == ReferenceRegisterWriteResult.Executed);
        Assert.Single(results, result => result == ReferenceRegisterWriteResult.AlreadyCompleted);
        var writes = services.GetRequiredService<IReferenceRegisterIndependentWriteService>();
        var reads = services.GetRequiredService<IReferenceRegisterReadService>();
        Assert.Single(await reads.GetKeyHistoryAsync(register, keys[0], DateTime.UtcNow));
        foreach (var key in keys.Skip(1))
            await writes.UpsertAsync(register, key, null, Values(7m), Guid.CreateVersion7());
        var all = await reads.SliceLastAllAsync(register, DateTime.UtcNow);
        Assert.Equal(4, all.Count);
        await writes.TombstoneByDimensionSetIdAsync(register, all[0].DimensionSetId, DateTime.UtcNow, Guid.CreateVersion7());
        await writes.TombstoneByDimensionSetIdAsync(register, all[2].DimensionSetId, DateTime.UtcNow, Guid.CreateVersion7());
        var visible = new List<ReferenceRegisterRecordRead>();
        var cursors = new HashSet<Guid>();
        Guid? cursor = null;
        var asOf = DateTime.UtcNow;
        ReferenceRegisterSlicePage<ReferenceRegisterRecordRead> page;
        do
        {
            page = await reads.SliceLastAllPageAsync(register, asOf, afterDimensionSetId: cursor, limit: 1);
            Assert.InRange(page.Records.Count, 0, 1);
            visible.AddRange(page.Records);
            cursor = page.NextAfterDimensionSetId;
            if (page.HasMore)
            {
                Assert.NotNull(cursor);
                Assert.True(cursors.Add(cursor.Value));
            }
            Assert.InRange(visible.Count, 0, 2);
        } while (page.HasMore);
        Assert.Equal(new[] { all[1].RecordId, all[3].RecordId }, visible.Select(row => row.RecordId));
        var enriched = await reads.SliceLastAllEnrichedPageAsync(register, asOf, limit: 10, includeDeleted: true);
        Assert.Equal(4, enriched.Records.Count);
        Assert.All(enriched.Records, row => Assert.StartsWith("Paged rate bench ",
            row.DimensionValueDisplaysByDimensionId[EquipmentDefinitions.EquipmentDimensionId]));
        var filtered = await reads.SliceLastAllFilteredPageAsync(register, asOf, keys[0], limit: 1, includeDeleted: true);
        Assert.Single(filtered.Records);
        var audit = await services.GetRequiredService<IAuditEventReader>().QueryAsync(
            new AuditLogQuery(EntityKind: AuditEntityKind.ReferenceRegister, EntityId: register));
        Assert.Equal(4, audit.Count(row => row.ActionCode == "refreg.records.upsert"));
        Assert.Equal(2, audit.Count(row => row.ActionCode == "refreg.records.tombstone"));
    }

    private static async Task<Guid> ConfigureAsync(IServiceProvider services, string code, ReferenceRegisterPeriodicity periodicity)
    {
        var management = services.GetRequiredService<IReferenceRegisterManagementService>();
        var id = await management.UpsertAsync(code, "Equipment reference amounts", periodicity, ReferenceRegisterRecordMode.Independent);
        await management.ReplaceFieldsAsync(id, [new ReferenceRegisterFieldDefinition("amount", "Amount", 1, ColumnType.Decimal, false)]);
        await management.ReplaceDimensionRulesAsync(id,
            [new ReferenceRegisterDimensionRule(EquipmentDefinitions.EquipmentDimensionId, "example.equipment", 1, true)]);
        await services.GetRequiredService<IReferenceRegisterAdminMaintenanceService>().EnsurePhysicalSchemaByIdAsync(id);
        return id;
    }

    private static async Task<DimensionValue[]> CreateEquipmentKeyAsync(InspectionHost host, string name)
    {
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", name)));
        return [new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, equipment.Id)];
    }

    private static Dictionary<string, object?> Values(decimal amount) => new() { ["amount"] = amount };

    private static async Task<List<ReferenceRegisterRecordRead>> ReadHistoryAsync(
        IReferenceRegisterReadService reads, Guid register, Guid dimensionSet, DateTime asOf, DateTime period)
    {
        var records = new List<ReferenceRegisterRecordRead>();
        DateTime? beforeTime = null;
        long? beforeId = null;
        while (true)
        {
            var page = await reads.GetKeyHistoryByDimensionSetIdAsync(register, dimensionSet, asOf, periodUtc: period,
                beforeRecordedAtUtc: beforeTime, beforeRecordId: beforeId, limit: 1, includeDeleted: true);
            if (page.Count == 0)
                break;

            var record = Assert.Single(page);
            records.Add(record);
            Assert.InRange(records.Count, 1, 6);
            beforeTime = record.RecordedAtUtc;
            beforeId = record.RecordId;
        }

        Assert.Equal(records.Count, records.Select(row => row.RecordId).Distinct().Count());
        return records;
    }
}
