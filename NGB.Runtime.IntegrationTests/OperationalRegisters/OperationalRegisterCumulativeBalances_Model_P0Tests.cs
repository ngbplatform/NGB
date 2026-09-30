using System.Collections.Concurrent;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

public sealed partial class OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests
{
    public static IEnumerable<object[]> ModelSeeds => Enumerable.Range(0, 16).Select(index => new object[] { 20260927 + index * 7919 });

    [Theory]
    [MemberData(nameof(ModelSeeds))]
    public async Task Concurrent_lifecycle_matches_independent_document_model_for_every_month_dimension_and_resource(int seed)
    {
        var random = new Random(seed);
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var id = Guid.CreateVersion7();
        var resources = new[] { "amount", "quantity", "discount" };
        await SeedRegisterAsync(host, id, "it_lifecycle_" + id.ToString("N"), resources.Select((code, i) => new OperationalRegisterResourceDefinition(code, code, i + 1)).ToArray());
        var dimensions = await ModelDimensionsAsync(host, id);
        var months = Enumerable.Range(-1, 6).Select(offset => January.AddMonths(offset)).ToArray();
        var docs = Enumerable.Range(0, 24).Select(_ => Guid.CreateVersion7()).ToArray();
        var initial = docs.ToDictionary(doc => doc, doc => RandomModelRows(random, doc, dimensions));
        var revised = docs.ToDictionary(doc => doc, doc => RandomModelRows(random, doc, dimensions));
        foreach (var doc in docs) await SeedDocumentAsync(host, doc, JanuaryDate);
        var expected = new ConcurrentDictionary<Guid, OperationalRegisterMovement[]>();
        foreach (var doc in docs.Take(12))
        {
            await ModelApplyAsync(host, id, doc, OperationalRegisterWriteOperation.Post, initial[doc]);
            expected[doc] = initial[doc];
        }
        foreach (var month in months) await MarkMonthDirtyAsync(host, id, month);
        (await FinalizeDirtyAsync(host, id)).Should().Be(months.Length);
        await AssertDocumentModelAsync(host, id, months, expected.Values.SelectMany(x => x).ToArray());
        var oldSnapshot = expected.Values.SelectMany(x => x).ToArray();
        await MarkMonthDirtyAsync(host, id, months[0]);
        gate.Arm();
        var finalizing = FinalizeDirtyAsync(host, id);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Parallel.ForEachAsync(docs, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (doc, ct) =>
            {
                var index = Array.IndexOf(docs, doc);
                var operation = index >= 12 ? OperationalRegisterWriteOperation.Post
                    : index % 2 == 0 ? OperationalRegisterWriteOperation.Repost : OperationalRegisterWriteOperation.Unpost;
                var rows = operation == OperationalRegisterWriteOperation.Unpost ? [] : revised[doc];
                await ModelApplyAsync(host, id, doc, operation, rows);
                expected[doc] = rows;
                // Retrying the same command must not change the logical model or add movements.
                (await ModelApplyResultAsync(host, id, doc, operation, rows)).Should().Be(OperationalRegisterWriteResult.AlreadyCompleted);
            });
            // A real business transaction that rolls back must not contribute to either model or publication.
            var rolledBack = Guid.CreateVersion7();
            await SeedDocumentAsync(host, rolledBack, JanuaryDate);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                await uow.BeginTransactionAsync();
                await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>()
                    .ApplyMovementsForDocumentAsync(id, rolledBack, OperationalRegisterWriteOperation.Post,
                        RandomModelRows(random, rolledBack, dimensions), manageTransaction: false);
                await uow.RollbackAsync();
            }
            // The writer has committed, but preparation is private until publication.
            await AssertDocumentModelAsync(host, id, months, oldSnapshot, finalized: false);
        }
        finally { gate.Release.TrySetResult(); }
        (await finalizing.WaitAsync(TimeSpan.FromSeconds(20))).Should().Be(months.Length);
        var desired = expected.Values.SelectMany(x => x).ToArray();
        await AssertDocumentModelAsync(host, id, months, desired);
        var versions = await ProjectionVersionsAsync(host, id);
        await MarkMonthDirtyAsync(host, id, months[0]);
        (await FinalizeDirtyAsync(host, id)).Should().Be(months.Length);
        await AssertDocumentModelAsync(host, id, months, desired);
        (await ProjectionVersionsAsync(host, id)).Order().Should().Equal(versions.Order());
    }

    private static OperationalRegisterMovement[] RandomModelRows(Random random, Guid doc, Guid[] dimensions)
        => Enumerable.Range(0, random.Next(1, 5)).Select(_ => new OperationalRegisterMovement(doc,
            JanuaryDate.AddMonths(random.Next(-1, 5)), dimensions[random.Next(dimensions.Length)],
            new Dictionary<string, decimal>
            {
                ["amount"] = random.Next(-10000, 10001) / 10000m,
                ["quantity"] = random.Next(-500, 501) / 16m,
                ["discount"] = random.Next(3) == 0 ? 0m : random.Next(-100, 101) / 100m
            })).ToArray();

    private static async Task<Guid[]> ModelDimensionsAsync(IHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        var dimension = Guid.CreateVersion7();
        var code = "model_" + dimension.ToString("N");
        await uow.Connection.ExecuteAsync("INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, 'Model')",
            new { Id = dimension, Code = code }, transaction: uow.Transaction);
        await scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>()
            .ReplaceAsync(id, [new(dimension, code, 1, false)], DateTime.UtcNow);
        var sets = new List<Guid> { Guid.Empty };
        for (var i = 0; i < 4; i++) sets.Add(await scope.ServiceProvider.GetRequiredService<IDimensionSetService>()
            .GetOrCreateIdAsync(new DimensionBag([new(dimension, Guid.CreateVersion7())])));
        await uow.CommitAsync();
        return sets.ToArray();
    }

    private static async Task ModelApplyAsync(IHost host, Guid id, Guid doc, OperationalRegisterWriteOperation operation, OperationalRegisterMovement[] rows)
        => (await ModelApplyResultAsync(host, id, doc, operation, rows)).Should().Be(OperationalRegisterWriteResult.Executed);

    private static async Task<OperationalRegisterWriteResult> ModelApplyResultAsync(IHost host, Guid id, Guid doc,
        OperationalRegisterWriteOperation operation, OperationalRegisterMovement[] rows)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>()
            .ApplyMovementsForDocumentAsync(id, doc, operation, rows);
    }

    private static async Task AssertDocumentModelAsync(IHost host, Guid id, DateOnly[] months,
        OperationalRegisterMovement[] currentDocuments, bool finalized = true)
    {
        // Oracle is the intended current state of documents, not persisted storno rows or projection SQL.
        await using var scope = host.Services.CreateAsyncScope();
        foreach (var month in months)
        {
            foreach (var cumulative in new[] { false, true })
            {
                var expected = new Dictionary<Guid, Dictionary<string, decimal>>();
                foreach (var row in currentDocuments)
                {
                    var rowMonth = new DateOnly(row.OccurredAtUtc.Year, row.OccurredAtUtc.Month, 1);
                    if (cumulative ? rowMonth > month : rowMonth != month) continue;
                    if (!expected.TryGetValue(row.DimensionSetId, out var sums)) expected[row.DimensionSetId] = sums = new();
                    foreach (var pair in row.Resources) sums[pair.Key] = sums.GetValueOrDefault(pair.Key) + pair.Value;
                }
                foreach (var empty in expected.Where(pair => pair.Value.Values.All(value => value == 0)).Select(pair => pair.Key).ToArray()) expected.Remove(empty);
                var actual = cumulative
                    ? await scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>().GetByMonthAsync(id, month)
                    : await scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>().GetByMonthAsync(id, month);
                actual.ToDictionary(row => row.DimensionSetId, row => row.Values).Should().BeEquivalentTo(expected,
                    $"month={month}, cumulative={cumulative}");
            }
            if (finalized)
                (await scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>().GetAsync(id, month))!
                    .Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
        }
    }
}
