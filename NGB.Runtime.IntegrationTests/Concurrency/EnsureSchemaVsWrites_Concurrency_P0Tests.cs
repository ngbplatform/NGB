using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Metadata.Base;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

/// <summary>
/// P0: ensure_schema background jobs (refreg/opreg) must be safe to run concurrently with real writes under load.
///
/// These tests exercise the same code paths the jobs use:
/// - refreg.ensure_schema => <see cref="IReferenceRegisterAdminMaintenanceService"/>.EnsurePhysicalSchemaForAllAsync
/// - opreg.ensure_schema  => <see cref="IOperationalRegisterAdminMaintenanceService"/>.EnsurePhysicalSchemaForAllAsync
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EnsureSchemaVsWrites_Concurrency_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RefReg_EnsureSchemaForAll_ConcurrentWithIndependentUpserts_UnderLoad_DoesNotDeadlock_AndAppendsRecords()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Arrange: register with enough metadata to make EnsureSchema heavy.
        const string code = "RR_LOAD";
        var dimensionId = DeterministicGuid.Create("Dimension|building");

        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "Reference Register Load",
                periodicity: ReferenceRegisterPeriodicity.Day,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(
                registerId,
                [
                    new ReferenceRegisterDimensionRule(
                        DimensionId: dimensionId,
                        DimensionCode: "building",
                        Ordinal: 10,
                        IsRequired: true),
                ],
                ct: CancellationToken.None);

            // Lots of fields => more DDL/ALTER TABLE work.
            var fields = new List<ReferenceRegisterFieldDefinition>();
            fields.Add(new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, IsNullable: true));
            fields.Add(new ReferenceRegisterFieldDefinition("note", "Note", 20, ColumnType.String, IsNullable: true));
            fields.Add(new ReferenceRegisterFieldDefinition("qty", "Qty", 30, ColumnType.Int32, IsNullable: true));
            for (var i = 4; i <= 18; i++)
                fields.Add(new ReferenceRegisterFieldDefinition($"f{i:00}", $"F{i:00}", i * 10, ColumnType.String, IsNullable: true));

            await mgmt.ReplaceFieldsAsync(registerId, fields, ct: CancellationToken.None);
        }

        // Multiple keys to avoid all writers serializing on the same per-key lock.
        var valueIds = Enumerable.Range(1, 24).Select(i => DeterministicGuid.Create($"It|RR|building|{i:00}")).ToArray();

        const int writerWorkers = 12;
        const int ensureWorkers = 2;
        const int upsertsPerWriter = 25;
        const int ensureRunsPerWorker = 10;

        var totalWorkers = writerWorkers + ensureWorkers;
        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;

        async Task Writer(int workerIndex)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var writes = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            if (Interlocked.Increment(ref ready) == totalWorkers)
                allReady.TrySetResult();

            await allReady.Task;
            await go.Task;

            for (var i = 0; i < upsertsPerWriter; i++)
            {
                var valueId = valueIds[(workerIndex + i) % valueIds.Length];

                var dims = new[] { new DimensionValue(dimensionId, valueId) };

                // Periodicity=Day => needs periodUtc.
                var periodUtc = new DateTime(2026, 01, 15 + (i % 10), 0, 0, 0, DateTimeKind.Utc);

                var values = new Dictionary<string, object?>
                {
                    ["amount"] = (decimal)(workerIndex * 1000 + i),
                    ["note"] = $"w{workerIndex}-i{i}",
                    ["qty"] = i,
                };

                await writes.UpsertAsync(
                    registerId,
                    dimensions: dims,
                    periodUtc,
                    values,
                    commandId: Guid.CreateVersion7(),
                    manageTransaction: true,
                    ct: CancellationToken.None);

                // Jitter helps interleave DDL / writes in different phases.
                if ((i & 3) == 0)
                    await Task.Delay(Random.Shared.Next(0, 5));
            }
        }

        async Task Ensurer()
        {
            await using var scope = host.Services.CreateAsyncScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminMaintenanceService>();

            if (Interlocked.Increment(ref ready) == totalWorkers)
                allReady.TrySetResult();

            await allReady.Task;
            await go.Task;

            for (var i = 0; i < ensureRunsPerWorker; i++)
            {
                var report = await maintenance.EnsurePhysicalSchemaForAllAsync(CancellationToken.None);
                report.TotalCount.Should().Be(1);

                if ((i & 1) == 0)
                    await Task.Delay(Random.Shared.Next(0, 10));
            }
        }

        var tasks = new List<Task>(capacity: totalWorkers);

        for (var i = 0; i < writerWorkers; i++)
            tasks.Add(Writer(i));

        for (var i = 0; i < ensureWorkers; i++)
            tasks.Add(Ensurer());

        await allReady.Task;
        go.TrySetResult();

        Func<Task> act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();

        // Assert: writes were not blocked into no-op and physical table exists.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();
            reg!.HasRecords.Should().BeTrue();

            var table = ReferenceRegisterNaming.RecordsTable(reg.TableCode);

            var count = await uow.Connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    $"SELECT COUNT(*) FROM {table};",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            count.Should().Be(writerWorkers * upsertsPerWriter);
        }
    }

    [Fact]
    public async Task OpReg_EnsureSchemaForAll_ConcurrentWithMovementsPosting_UnderLoad_DoesNotDeadlock_AndAppendsMovements()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;

        // Arrange: operational register with many resources to make EnsureSchema heavy.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            registerId = await mgmt.UpsertAsync("OR_LOAD", name: "Operational Register Load", CancellationToken.None);

            var resources = new List<OperationalRegisterResourceDefinition>
            {
                new("Amount", "Amount", Ordinal: 10)
            };

            for (var i = 2; i <= 18; i++)
                resources.Add(new OperationalRegisterResourceDefinition($"AMOUNT_{i:00}", $"Amount {i:00}", Ordinal: i * 10));

            await mgmt.ReplaceResourcesAsync(registerId, resources, CancellationToken.None);

            // No dimension rules => DimensionSetId must be Guid.Empty.
            await mgmt.ReplaceDimensionRulesAsync(registerId, [], CancellationToken.None);
        }

        // Distribute writes across months to reduce contention on (registerId, month) advisory locks.
        var months = new[]
        {
            new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 02, 10, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 03, 10, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 04, 10, 0, 0, 0, DateTimeKind.Utc),
        };

        const int writerWorkers = 12;
        const int ensureWorkers = 2;
        const int docsPerWriter = 20;
        const int movementsPerDoc = 2;
        const int ensureRunsPerWorker = 10;

        var totalWorkers = writerWorkers + ensureWorkers;
        var allReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;

        async Task Writer(int workerIndex)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            if (Interlocked.Increment(ref ready) == totalWorkers)
                allReady.TrySetResult();

            await allReady.Task;
            await go.Task;

            for (var i = 0; i < docsPerWriter; i++)
            {
                var documentId = Guid.CreateVersion7();
                var occurredAtUtc = months[(workerIndex + i) % months.Length];

                // Operational registers are document-bound and enforce FK(document_id => documents.id).
                // For this concurrency test we create a minimal draft header per synthetic documentId.
                var nowUtc = DateTime.UtcNow;
                await uow.BeginTransactionAsync(CancellationToken.None);
                await docs.CreateAsync(new DocumentRecord
                {
                    Id = documentId,
                    TypeCode = "IT",
                    Number = $"OR_LOAD-{workerIndex:00}-{i:0000}",
                    DateUtc = occurredAtUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                    PostedAtUtc = null,
                    MarkedForDeletionAtUtc = null
                }, CancellationToken.None);
                await uow.CommitAsync(CancellationToken.None);

                var moves = new List<OperationalRegisterMovement>(capacity: movementsPerDoc);
                for (var m = 0; m < movementsPerDoc; m++)
                {
                    moves.Add(new OperationalRegisterMovement(
                        DocumentId: documentId,
                        OccurredAtUtc: occurredAtUtc.AddMinutes(m),
                        DimensionSetId: Guid.Empty,
                        Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                        {
                            ["amount"] = workerIndex * 1000m + i * 10m + m
                        }));
                }

                await applier.ApplyMovementsForDocumentAsync(
                    registerId,
                    documentId,
                    OperationalRegisterWriteOperation.Post,
                    moves,
                    affectedPeriods: null,
                    manageTransaction: true,
                    ct: CancellationToken.None);

                if ((i & 3) == 0)
                    await Task.Delay(Random.Shared.Next(0, 5));
            }
        }

        async Task Ensurer()
        {
            await using var scope = host.Services.CreateAsyncScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();

            if (Interlocked.Increment(ref ready) == totalWorkers)
                allReady.TrySetResult();

            await allReady.Task;
            await go.Task;

            for (var i = 0; i < ensureRunsPerWorker; i++)
            {
                var report = await maintenance.EnsurePhysicalSchemaForAllAsync(CancellationToken.None);
                report.TotalCount.Should().Be(1);

                if ((i & 1) == 0)
                    await Task.Delay(Random.Shared.Next(0, 10));
            }
        }

        var tasks = new List<Task>(capacity: totalWorkers);

        for (var i = 0; i < writerWorkers; i++)
            tasks.Add(Writer(i));

        for (var i = 0; i < ensureWorkers; i++)
            tasks.Add(Ensurer());

        await allReady.Task;
        go.TrySetResult();

        Func<Task> act = () => Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();

        // Assert: movements were appended.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();
            reg!.HasMovements.Should().BeTrue();

            var table = OperationalRegisterNaming.MovementsTable(reg.TableCode);

            var count = await uow.Connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    $"SELECT COUNT(*) FROM {table};",
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            count.Should().Be(writerWorkers * docsPerWriter * movementsPerDoc);
        }
    }
}
