using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.AuditLog;
using NGB.Metadata.Base;
using NGB.Persistence.AuditLog;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(RegistersPostgresCollection.Name)]
public sealed class ReferenceRegisterIndependentWriteService_ClockSkew_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime EffectiveAt = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AllRecordedVersions = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

    public static IEnumerable<object[]> Periodicities() =>
        Enum.GetValues<ReferenceRegisterPeriodicity>().Select(p => new object[] { p });

    public static IEnumerable<object[]> ClockCases() =>
        from p in Enum.GetValues<ReferenceRegisterPeriodicity>()
        from hours in new[] { -24, 24 }
        select new object[] { p, hours };

    public static IEnumerable<object[]> TransactionCases() =>
        from row in ClockCases()
        from manage in new[] { true, false }
        select row.Concat(new object[] { manage }).ToArray();

    public static IEnumerable<object[]> PeriodicCases() =>
        Periodicities().Where(row => (ReferenceRegisterPeriodicity)row[0] != ReferenceRegisterPeriodicity.NonPeriodic);

    [Theory]
    [MemberData(nameof(TransactionCases))]
    public async Task Tombstone_WithClockSkew_CommitsCopiedVersion_AndRemainsIdempotent(
        ReferenceRegisterPeriodicity periodicity, int clockOffsetHours, bool manageTransaction)
    {
        var clock = new OffsetTimeProvider(clockOffsetHours);
        using var host = CreateHost(clock);
        var registerId = await ArrangeAsync(host, periodicity);
        var period = Period(periodicity);
        await UpsertAsync(host, registerId, period, 42);
        var original = (await HistoryAsync(host, registerId, period)).Should().ContainSingle().Subject;
        if (clockOffsetHours < 0)
            original.RecordedAtUtc.Should().BeAfter(clock.GetUtcNow().UtcDateTime,
                "the database-written version must be ahead of the application clock in this regression");

        var commandId = Guid.CreateVersion7();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            if (!manageTransaction)
                await uow.BeginTransactionAsync();

            (await writer.TombstoneAsync(registerId, [], EffectiveAt, commandId, manageTransaction))
                .Should().Be(ReferenceRegisterWriteResult.Executed);

            if (!manageTransaction)
                await uow.CommitAsync();
        }

        var versions = await HistoryAsync(host, registerId, period);
        versions.Should().HaveCount(2, "a committed active version must not be mistaken for an absent key");
        versions[0].IsDeleted.Should().BeTrue();
        versions[0].Values["amount"].Should().Be(42);
        versions[0].PeriodUtc.Should().Be(period);
        versions[1].Should().BeEquivalentTo(original);

        await using var verify = host.Services.CreateAsyncScope();
        var read = verify.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        // Use the actual database timestamp, not an arbitrary wall-clock delay.
        (await read.SliceLastByDimensionSetIdAsync(registerId, Guid.Empty, versions[0].RecordedAtUtc))
            .Should().BeNull();
        (await read.SliceLastByDimensionSetIdAsync(registerId, Guid.Empty,
            versions[0].RecordedAtUtc, includeDeleted: true))!.IsDeleted.Should().BeTrue();
        var historical = await verify.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>()
            .SliceLastForEffectiveMomentAsync(registerId, Guid.Empty, EffectiveAt, original.RecordedAtUtc);
        historical.Should().BeEquivalentTo(original, "explicit historical reads must retain their recorded-time cutoff");

        var retry = verify.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
        (await retry.TombstoneByDimensionSetIdAsync(registerId, Guid.Empty, EffectiveAt, commandId))
            .Should().Be(ReferenceRegisterWriteResult.AlreadyCompleted);
        (await retry.TombstoneByDimensionSetIdAsync(registerId, Guid.Empty, EffectiveAt, Guid.CreateVersion7()))
            .Should().Be(ReferenceRegisterWriteResult.Executed, "an already deleted key is a no-op");
        (await HistoryAsync(host, registerId, period)).Should().HaveCount(2);
        (await AuditAsync(host, registerId, AuditActionCodes.ReferenceRegisterRecordsTombstone)).Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(Periodicities))]
    public async Task Tombstone_WithClockBehind_RollbackRemovesVersionAuditAndCommand_AndAllowsRetry(
        ReferenceRegisterPeriodicity periodicity)
    {
        using var host = CreateHost(new OffsetTimeProvider(-24));
        var registerId = await ArrangeAsync(host, periodicity);
        var period = Period(periodicity);
        await UpsertAsync(host, registerId, period, 42);
        var commandId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
            var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            try
            {
                (await writer.TombstoneByDimensionSetIdAsync(registerId, Guid.Empty, EffectiveAt,
                    commandId, manageTransaction: false)).Should().Be(ReferenceRegisterWriteResult.Executed);
                (await reader.ListKeyHistoryAsync(registerId, Guid.Empty, AllRecordedVersions, period))
                    .Should().HaveCount(2);
                (await scope.ServiceProvider.GetRequiredService<IAuditEventReader>().QueryAsync(
                    AuditQuery(registerId, AuditActionCodes.ReferenceRegisterRecordsTombstone)))
                    .Should().ContainSingle();
            }
            finally
            {
                await uow.RollbackAsync();
            }
        }

        (await HistoryAsync(host, registerId, period)).Should().ContainSingle().Which.IsDeleted.Should().BeFalse();
        (await AuditAsync(host, registerId, AuditActionCodes.ReferenceRegisterRecordsTombstone)).Should().BeEmpty();
        await using var retryScope = host.Services.CreateAsyncScope();
        (await retryScope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>()
            .TombstoneByDimensionSetIdAsync(registerId, Guid.Empty, EffectiveAt, commandId))
            .Should().Be(ReferenceRegisterWriteResult.Executed, "rollback must also remove the idempotency entry");
        (await HistoryAsync(host, registerId, period)).Should().HaveCount(2);
        (await AuditAsync(host, registerId, AuditActionCodes.ReferenceRegisterRecordsTombstone)).Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(ClockCases))]
    public async Task Upsert_WithClockSkew_AuditsTheCommittedPreviousValue(
        ReferenceRegisterPeriodicity periodicity, int clockOffsetHours)
    {
        using var host = CreateHost(new OffsetTimeProvider(clockOffsetHours));
        var registerId = await ArrangeAsync(host, periodicity);
        var period = Period(periodicity);
        await UpsertAsync(host, registerId, period, 42);
        await UpsertAsync(host, registerId, period, 99);

        var events = await AuditAsync(host, registerId, AuditActionCodes.ReferenceRegisterRecordsUpsert);
        events.Should().HaveCount(2);
        var update = events.Single(e => e.Changes.Any(c => c.FieldPath == "values" && Amount(c.NewValueJson) == 99));
        Amount(update.Changes.Single(c => c.FieldPath == "values").OldValueJson).Should().Be(42,
            "clock skew must not turn an update into an apparent insertion in the audit trail");
        (await HistoryAsync(host, registerId, period)).Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(Periodicities))]
    public async Task UpsertThenTombstone_InSameOuterTransaction_WithClockBehind_ReadsOwnWrites(
        ReferenceRegisterPeriodicity periodicity)
    {
        using var host = CreateHost(new OffsetTimeProvider(-24));
        var registerId = await ArrangeAsync(host, periodicity);
        var period = Period(periodicity);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
            await uow.BeginTransactionAsync();
            await writer.UpsertAsync(registerId, [], period, Values(42), Guid.CreateVersion7(), manageTransaction: false);
            await writer.TombstoneAsync(registerId, [], EffectiveAt, Guid.CreateVersion7(), manageTransaction: false);
            await uow.CommitAsync();
        }
        var versions = await HistoryAsync(host, registerId, period);
        versions.Should().HaveCount(2);
        versions[0].IsDeleted.Should().BeTrue();
        versions[0].Values["amount"].Should().Be(42);
        (await AuditAsync(host, registerId, AuditActionCodes.ReferenceRegisterRecordsTombstone)).Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(PeriodicCases))]
    public async Task BackdatedTombstone_WithClockBehind_DeletesEffectiveVersion_AndPreservesFuturePeriod(
        ReferenceRegisterPeriodicity periodicity)
    {
        using var host = CreateHost(new OffsetTimeProvider(-24));
        var registerId = await ArrangeAsync(host, periodicity);
        var future = EffectiveAt.AddYears(1);
        await UpsertAsync(host, registerId, EffectiveAt, 42);
        await UpsertAsync(host, registerId, future, 99);
        await using var scope = host.Services.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
        var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();

        // No effective record before the first period: this must remain a no-op.
        await writer.TombstoneAsync(registerId, [], EffectiveAt.AddTicks(-1), Guid.CreateVersion7());
        (await HistoryAsync(host, registerId, EffectiveAt)).Should().ContainSingle();
        (await AuditAsync(host, registerId, AuditActionCodes.ReferenceRegisterRecordsTombstone)).Should().BeEmpty();

        await writer.TombstoneAsync(registerId, [], EffectiveAt, Guid.CreateVersion7());
        var deleted = await reader.SliceLastForEffectiveMomentAsync(registerId, Guid.Empty, EffectiveAt, AllRecordedVersions);
        deleted.Should().NotBeNull();
        deleted!.IsDeleted.Should().BeTrue();
        deleted.Values["amount"].Should().Be(42);
        deleted.PeriodUtc.Should().Be(EffectiveAt);
        var later = await reader.SliceLastForEffectiveMomentAsync(registerId, Guid.Empty, future, AllRecordedVersions);
        later!.IsDeleted.Should().BeFalse();
        later.Values["amount"].Should().Be(99);
        (await HistoryAsync(host, registerId, future)).Should().ContainSingle();
    }

    private IHost CreateHost(TimeProvider clock) =>
        IntegrationHostFactory.Create(Fixture.ConnectionString, services => services.AddSingleton(clock));

    private static async Task<Guid> ArrangeAsync(IHost host, ReferenceRegisterPeriodicity periodicity)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        var registerId = await management.UpsertAsync("RR_CLOCK_SKEW", "Clock skew regression", periodicity,
            ReferenceRegisterRecordMode.Independent);
        await management.ReplaceFieldsAsync(registerId,
            [new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Int32, false)]);
        return registerId;
    }

    private static DateTime? Period(ReferenceRegisterPeriodicity periodicity) =>
        periodicity == ReferenceRegisterPeriodicity.NonPeriodic ? null : EffectiveAt;

    private static Dictionary<string, object?> Values(int amount) => new() { ["amount"] = amount };

    private static async Task UpsertAsync(IHost host, Guid registerId, DateTime? period, int amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>()
            .UpsertAsync(registerId, [], period, Values(amount), Guid.CreateVersion7()))
            .Should().Be(ReferenceRegisterWriteResult.Executed);
    }

    private static async Task<IReadOnlyList<ReferenceRegisterRecordRead>> HistoryAsync(IHost host, Guid registerId, DateTime? period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>()
            .ListKeyHistoryAsync(registerId, Guid.Empty, AllRecordedVersions, period);
    }

    private static AuditLogQuery AuditQuery(Guid registerId, string action) =>
        new(EntityKind: AuditEntityKind.ReferenceRegister, EntityId: registerId, ActionCode: action, Limit: 50);

    private static async Task<IReadOnlyList<AuditEvent>> AuditAsync(IHost host, Guid registerId, string action)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAuditEventReader>().QueryAsync(AuditQuery(registerId, action));
    }

    private static int? Amount(string? json)
    {
        if (json is null or "null")
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("amount").GetInt32();
    }

    private sealed class OffsetTimeProvider(int hours) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow().AddHours(hours);
    }
}
