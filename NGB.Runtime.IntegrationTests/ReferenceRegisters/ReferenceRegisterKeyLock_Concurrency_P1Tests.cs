using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P1: Key-level advisory locking for Reference Register independent writes.
///
/// Contract:
/// - Same (registerId, dimensionSetId) writes must serialize.
/// - Different dimensionSetId keys must not block each other.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterKeyLock_Concurrency_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SameKey_IsSerialized_ByKeyLock()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_KEYLOCK_SAMEKEY";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var setA = await CreateBuildingDimensionSetIdAsync(host, buildingCode: "A");

        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Task #1 holds the advisory lock for the key inside an open transaction.
        var hold = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var keyLock = scope.ServiceProvider.GetRequiredService<IReferenceRegisterKeyLock>();

            await uow.ExecuteInUowTransactionAsync(
                manageTransaction: true,
                async ct =>
                {
                    await keyLock.LockKeyAsync(registerId, setA, ct);
                    lockAcquired.TrySetResult();
                    await releaseLock.Task;
                    return 0;
                },
                CancellationToken.None);
        });

        await lockAcquired.Task;

        // Task #2 tries to write the SAME key while the lock is held. It must block until we release.
        var started = DateTime.UtcNow;

        var write = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            return await svc.UpsertByDimensionSetIdAsync(
                registerId,
                setA,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 1 },
                commandId: Guid.CreateVersion7(),
                manageTransaction: true,
                ct: CancellationToken.None);
        });

        // Give it a moment to attempt lock acquisition.
        await Task.Delay(200);
        write.IsCompleted.Should().BeFalse("same key must be blocked by the key-level lock");

        // Release the lock transaction.
        releaseLock.TrySetResult();

        (await write).Should().Be(ReferenceRegisterWriteResult.Executed);
        await hold;

        var elapsedMs = (long)(DateTime.UtcNow - started).TotalMilliseconds;
        elapsedMs.Should().BeGreaterThan(150, "write should have waited for the lock to be released");

        // Sanity: the record exists.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var row = await read.SliceLastByDimensionSetIdAsync(
                registerId,
                setA,
                asOfUtc: DateTime.UtcNow,
                recorderDocumentId: null,
                includeDeleted: true,
                ct: CancellationToken.None);

            row.Should().NotBeNull();
            row!.Values["amount"].Should().Be(1);
        }
    }

    [Fact]
    public async Task DifferentKeys_DoNotBlockEachOther()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_KEYLOCK_DIFFKEYS";
        var registerId = await ArrangeIndependentRegisterAsync(host, code);

        var setA = await CreateBuildingDimensionSetIdAsync(host, buildingCode: "A");
        var setB = await CreateBuildingDimensionSetIdAsync(host, buildingCode: "B");

        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Hold lock for key A.
        var hold = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var keyLock = scope.ServiceProvider.GetRequiredService<IReferenceRegisterKeyLock>();

            await uow.ExecuteInUowTransactionAsync(
                manageTransaction: true,
                async ct =>
                {
                    await keyLock.LockKeyAsync(registerId, setA, ct);
                    lockAcquired.TrySetResult();
                    await releaseLock.Task;
                    return 0;
                },
                CancellationToken.None);
        });

        await lockAcquired.Task;

        // While A is locked, key B must still be writable.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            var resB = await svc.UpsertByDimensionSetIdAsync(
                registerId,
                setB,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 2 },
                commandId: Guid.CreateVersion7(),
                manageTransaction: true,
                ct: CancellationToken.None);

            resB.Should().Be(ReferenceRegisterWriteResult.Executed, "different key must not be blocked by another key lock");
        }

        // Now attempt a write for key A: must block until release.
        var writeA = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            return await svc.UpsertByDimensionSetIdAsync(
                registerId,
                setA,
                periodUtc: null,
                values: new Dictionary<string, object?> { ["amount"] = 1 },
                commandId: Guid.CreateVersion7(),
                manageTransaction: true,
                ct: CancellationToken.None);
        });

        await Task.Delay(200);
        writeA.IsCompleted.Should().BeFalse("key A is still locked");

        releaseLock.TrySetResult();

        (await writeA).Should().Be(ReferenceRegisterWriteResult.Executed);
        await hold;

        // Sanity: both keys exist.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            var a = await read.SliceLastByDimensionSetIdAsync(
                registerId,
                setA,
                asOfUtc: DateTime.UtcNow,
                recorderDocumentId: null,
                includeDeleted: true,
                ct: CancellationToken.None);

            var b = await read.SliceLastByDimensionSetIdAsync(
                registerId,
                setB,
                asOfUtc: DateTime.UtcNow,
                recorderDocumentId: null,
                includeDeleted: true,
                ct: CancellationToken.None);

            a.Should().NotBeNull();
            b.Should().NotBeNull();
            a!.Values["amount"].Should().Be(1);
            b!.Values["amount"].Should().Be(2);
        }
    }

    private static async Task<Guid> CreateBuildingDimensionSetIdAsync(IHost host, string buildingCode)
    {
        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var buildingValueId = DeterministicGuid.Create($"Building|{buildingCode}");

        var bag = new DimensionBag([new DimensionValue(buildingDimId, buildingValueId)]);
        if (bag.IsEmpty)
            return Guid.Empty;

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

        // Non-empty bags require an active UoW transaction so the mapping is persisted.
        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction: true,
            ct => svc.GetOrCreateIdAsync(bag, ct),
            CancellationToken.None);
    }

    private static async Task<Guid> ArrangeIndependentRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var registerId = await mgmt.UpsertAsync(
            code,
            name: $"{code} name",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct: CancellationToken.None);

        await mgmt.ReplaceFieldsAsync(
            registerId,
            fields:
            [
                new ReferenceRegisterFieldDefinition(
                    Code: "amount",
                    Name: "Amount",
                    Ordinal: 10,
                    ColumnType: ColumnType.Int32,
                    IsNullable: false)
            ],
            ct: CancellationToken.None);

        var dimId = DeterministicGuid.Create("Dimension|building");

        await mgmt.ReplaceDimensionRulesAsync(
            registerId,
            rules:
            [
                new ReferenceRegisterDimensionRule(
                    DimensionId: dimId,
                    DimensionCode: "building",
                    Ordinal: 10,
                    IsRequired: true)
            ],
            ct: CancellationToken.None);

        return registerId;
    }
}
