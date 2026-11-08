using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Documents;
using NGB.Runtime.ReferenceRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PlatformPostgresCollection.Name)]
public sealed class ReferenceRegisterVersionOrder_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(ReferenceRegisterPeriodicity.NonPeriodic)]
    [InlineData(ReferenceRegisterPeriodicity.Day)]
    public async Task RecorderTombstone_FromEarlierTransaction_RemainsTheLatestVersion(ReferenceRegisterPeriodicity periodicity)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var effective = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        DateTime? period = periodicity == ReferenceRegisterPeriodicity.NonPeriodic ? null : effective;
        await using var setup = host.Services.CreateAsyncScope();
        var documentId = await setup.ServiceProvider.GetRequiredService<IDocumentDraftService>()
            .CreateDraftAsync("test_doc", "REFERENCE-VERSION", effective);
        var management = setup.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        var registerId = await management.UpsertAsync("recorder_version_order", "Recorder version order",
            periodicity, ReferenceRegisterRecordMode.SubordinateToRecorder);
        await management.ReplaceFieldsAsync(registerId,
            [new ReferenceRegisterFieldDefinition("amount", "Amount", 1, ColumnType.Int32, false)]);

        async Task AppendAsync(int amount)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var writeUow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await writeUow.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>().LockDocumentAsync(documentId);
            await scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>().AppendAsync(registerId,
                [new ReferenceRegisterRecordWrite(Guid.Empty, period, documentId,
                    new Dictionary<string, object?> { ["amount"] = amount })]);
            await writeUow.CommitAsync();
        }

        await AppendAsync(100);
        await using var older = host.Services.CreateAsyncScope();
        var uow = older.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await uow.Connection.ExecuteScalarAsync<DateTime>(
            new CommandDefinition("SELECT CURRENT_TIMESTAMP;", transaction: uow.Transaction));
        await AppendAsync(200);

        await older.ServiceProvider.GetRequiredService<IAdvisoryLockManager>().LockDocumentAsync(documentId);
        var store = older.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
        await ((IReferenceRegisterRecorderTombstoneWriter)store)
            .AppendTombstonesForRecorderAsync(registerId, documentId, null);
        await uow.CommitAsync();

        await using var verify = host.Services.CreateAsyncScope();
        var records = verify.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();
        var latest = await records.SliceLastAsync(registerId, Guid.Empty,
            DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), documentId);
        latest!.IsDeleted.Should().BeTrue("the recorder tombstone was serialized after the last committed update");
        latest.Values["amount"].Should().Be(200);
    }

    [Theory]
    [InlineData(ReferenceRegisterPeriodicity.NonPeriodic, false)]
    [InlineData(ReferenceRegisterPeriodicity.NonPeriodic, true)]
    [InlineData(ReferenceRegisterPeriodicity.Day, false)]
    [InlineData(ReferenceRegisterPeriodicity.Day, true)]
    public async Task LaterCommittedUpdate_FromEarlierTransaction_MustBeVisibleAsLatest(
        ReferenceRegisterPeriodicity periodicity, bool tombstone)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        Guid registerId;
        var effective = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        DateTime? period = periodicity == ReferenceRegisterPeriodicity.NonPeriodic ? null : effective;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var management = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await management.UpsertAsync("version_order", "Version order", periodicity,
                ReferenceRegisterRecordMode.Independent);
            await management.ReplaceFieldsAsync(registerId,
                [new ReferenceRegisterFieldDefinition("amount", "Amount", 1, ColumnType.Int32, false)]);
            await scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>()
                .UpsertAsync(registerId, [], period, new Dictionary<string, object?> { ["amount"] = 100 }, Guid.NewGuid());
        }

        await using var older = host.Services.CreateAsyncScope();
        var uow = older.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await uow.Connection.ExecuteScalarAsync<DateTime>(
            new CommandDefinition("SELECT CURRENT_TIMESTAMP;", transaction: uow.Transaction));

        await using (var newer = host.Services.CreateAsyncScope())
        {
            await newer.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>()
                .UpsertAsync(registerId, [], period, new Dictionary<string, object?> { ["amount"] = 200 }, Guid.NewGuid());
        }

        await older.ServiceProvider.GetRequiredService<IReferenceRegisterKeyLock>()
            .LockKeyAsync(registerId, Guid.Empty);
        var before = await older.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>()
            .SliceLastForWriteAsync(registerId, Guid.Empty, effective);
        before!.Values["amount"].Should().Be(200);
        var writer = older.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
        if (tombstone)
            await writer.TombstoneAsync(registerId, [], effective, Guid.NewGuid(), manageTransaction: false);
        else
            await writer.UpsertAsync(registerId, [], period, new Dictionary<string, object?> { ["amount"] = 300 },
                Guid.NewGuid(), manageTransaction: false);
        await uow.CommitAsync();

        await using var verify = host.Services.CreateAsyncScope();
        var records = verify.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();
        var allTimes = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var history = await records.ListKeyHistoryAsync(registerId, Guid.Empty, allTimes, period);
        history.Should().HaveCount(3);
        var latest = await records.SliceLastAsync(registerId, Guid.Empty, allTimes);
        latest!.IsDeleted.Should().Be(tombstone);
        latest.Values["amount"].Should().Be(tombstone ? 200 : 300,
            "the write serialized after the committed value 200 must be the latest version");
        latest.RecordId.Should().Be(history.Max(row => row.RecordId));
        var historical = await records.SliceLastForEffectiveMomentAsync(
            registerId, Guid.Empty, effective, before.RecordedAtUtc);
        historical!.IsDeleted.Should().BeFalse();
        historical.Values["amount"].Should().Be(200);
    }
}
