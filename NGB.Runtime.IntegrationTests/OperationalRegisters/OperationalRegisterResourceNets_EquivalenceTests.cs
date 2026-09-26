using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(RegistersPostgresCollection.Name)]
public sealed class OperationalRegisterResourceNets_EquivalenceTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    public enum SnapshotState { NoTable, EmptyTable, Unfinalized, Finalized, Dirty, Blocked }

    public static IEnumerable<object[]> EquivalenceCases =>
        from state in Enum.GetValues<SnapshotState>()
        from seed in new[] { 1729, 4099 }
        select new object[] { state, seed };

    [Theory]
    [InlineData(SnapshotState.NoTable)]
    [InlineData(SnapshotState.EmptyTable)]
    [InlineData(SnapshotState.Unfinalized)]
    [InlineData(SnapshotState.Finalized)]
    [InlineData(SnapshotState.Dirty)]
    [InlineData(SnapshotState.Blocked)]
    public async Task Known_ledger_returns_164_and_16_25_regardless_of_snapshot_storage(SnapshotState state)
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString);
        var filter = scenario.Dimensions.Take(4).Select(d => new DimensionValue(d, Guid.NewGuid())).ToArray();
        var exact = await scenario.AddSetAsync(filter);
        var superset = await scenario.AddSetAsync([.. filter, new(scenario.Dimensions[4], Guid.NewGuid())]);

        // Manually calculated ledger, independent of both old and new queries:
        // amount: 100 + 40 + 20 - 5 + 7 + 7 - 2 - 3 = 164.
        // quantity: 10 + 4 + 2 - 1 + 1 + 1 - 0.5 - 0.25 = 16.25.
        var june = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var augustEnd = new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc);
        await scenario.AppendAsync(exact, june, new(100m, 10m));
        await scenario.AppendAsync(superset, june, new(40m, 4m));
        await scenario.AppendAsync(exact, augustEnd, new(20m, 2m));
        await scenario.AppendAsync(superset, augustEnd, new(-5m, -1m));
        await scenario.AppendAsync(exact, DeltaDate, new(7m, 1m));
        await scenario.AppendAsync(exact, DeltaDate, new(7m, 1m));
        await scenario.AppendAsync(exact, DeltaDate, new(-2m, -0.5m));
        await scenario.AppendAsync(superset, DeltaDate, new(-3m, -0.25m));
        var positiveStorno = await scenario.AppendAsync(exact, DeltaDate, new(999m, 999m));
        var negativeStorno = await scenario.AppendAsync(superset, DeltaDate, new(-888m, -888m));
        await scenario.Movements.AppendStornoByDocumentAsync(scenario.RegisterId, positiveStorno);
        await scenario.Movements.AppendStornoByDocumentAsync(scenario.RegisterId, negativeStorno);

        // Every pair must match in ONE set. Partial matches in separate sets,
        // a changed value and swapped dimension/value assignments must not count.
        var firstHalf = await scenario.AddSetAsync(filter.Take(2).ToArray());
        var secondHalf = await scenario.AddSetAsync(filter.Skip(2).ToArray());
        var changed = await scenario.AddSetAsync([filter[0], filter[1], filter[2], new(filter[3].DimensionId, Guid.NewGuid())]);
        var swapped = await scenario.AddSetAsync([new(filter[0].DimensionId, filter[1].ValueId), new(filter[1].DimensionId, filter[0].ValueId), .. filter.Skip(2)]);
        foreach (var excluded in new[] { firstHalf, secondHalf, changed, swapped })
            await scenario.AppendAsync(excluded, DeltaDate, new(5000m, 5000m));

        if (state != SnapshotState.NoTable)
        {
            await scenario.Balances.EnsureSchemaAsync(scenario.RegisterId);
            if (state != SnapshotState.EmptyTable)
            {
                // A finalized snapshot agrees with the ledger through August.
                // Other states contain a deliberately obsolete value that must be ignored.
                await scenario.Balances.ReplaceForMonthAsync(scenario.RegisterId, SnapshotMonth,
                    state == SnapshotState.Finalized
                        ? [new(exact, new Amounts(120m, 12m).Resources), new(superset, new Amounts(35m, 3m).Resources)]
                        : [new(exact, new Amounts(9999m, 9999m).Resources)]);
                if (state == SnapshotState.Finalized)
                    await scenario.Finalizations.MarkFinalizedAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, DateTime.UtcNow);
                if (state == SnapshotState.Dirty)
                    await scenario.Finalizations.MarkDirtyAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, DateTime.UtcNow);
                if (state == SnapshotState.Blocked)
                    await scenario.Finalizations.MarkBlockedNoProjectorAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, "Test", DateTime.UtcNow);
            }
        }

        // These are the primary contract assertions: no legacy SQL or model helper.
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "amount")).Should().Be(164m);
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "quantity")).Should().Be(16.25m);
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, filter.Reverse().ToArray(), "amount")).Should().Be(164m);
    }

    [Fact]
    public async Task Known_snapshot_only_and_movement_only_items_sum_to_95_without_inner_join_loss()
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString);
        var pair = new DimensionValue(scenario.Dimensions[0], Guid.NewGuid());
        var snapshotOnly = await scenario.AddSetAsync([pair, new(scenario.Dimensions[1], Guid.NewGuid())]);
        var movementOnly = await scenario.AddSetAsync([pair, new(scenario.Dimensions[1], Guid.NewGuid())]);
        var both = await scenario.AddSetAsync([pair, new(scenario.Dimensions[1], Guid.NewGuid())]);
        await scenario.Balances.ReplaceForMonthAsync(scenario.RegisterId, SnapshotMonth,
            [new(snapshotOnly, new Amounts(100m, 10m).Resources), new(both, new Amounts(-20m, -2m).Resources)]);
        await scenario.Finalizations.MarkFinalizedAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, DateTime.UtcNow);
        await scenario.AppendAsync(movementOnly, DeltaDate, new(10m, 1m));
        await scenario.AppendAsync(both, DeltaDate, new(5m, 0.5m));
        // 100 + (-20 + 5) + 10 = 95; 10 + (-2 + 0.5) + 1 = 9.5.
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, [pair], "amount")).Should().Be(95m);
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, [pair], "quantity")).Should().Be(9.5m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Known_snapshot_chain_ignores_invalid_later_baselines_and_preserves_zero(bool blocked)
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString);
        DimensionValue[] filter = [new(scenario.Dimensions[0], Guid.NewGuid())];
        var set = await scenario.AddSetAsync(filter);
        var june = new DateOnly(2026, 6, 1);
        var september = new DateOnly(2026, 9, 1);
        await scenario.AppendAsync(set, june.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), new(100m, 10m));
        await scenario.AppendAsync(set, SnapshotMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), new(20m, 2m));
        await scenario.AppendAsync(set, DeltaDate, new(-5m, -0.5m));
        await scenario.Balances.ReplaceForMonthAsync(scenario.RegisterId, june, [new(set, new Amounts(100m, 10m).Resources)]);
        await scenario.Finalizations.MarkFinalizedAsync(scenario.RegisterId, june, DateTime.UtcNow, DateTime.UtcNow);
        // September is marked finalized but its baseline is stale because August
        // is invalid. The reader must use June (100) + August (20) - September (5).
        await scenario.Balances.ReplaceForMonthAsync(scenario.RegisterId, september, [new(set, new Amounts(9999m, 9999m).Resources)]);
        await scenario.Finalizations.MarkFinalizedAsync(scenario.RegisterId, september, DateTime.UtcNow, DateTime.UtcNow);
        if (blocked)
            await scenario.Finalizations.MarkBlockedNoProjectorAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, "Test", DateTime.UtcNow);
        else
            await scenario.Finalizations.MarkDirtyAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, DateTime.UtcNow);
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "amount")).Should().Be(115m);
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "quantity")).Should().Be(11.5m);

        // Settle the remaining balance and finalize an empty (zero-pruned) snapshot.
        await scenario.AppendAsync(set, DeltaDate, new(-115m, -11.5m));
        await scenario.Balances.ReplaceForMonthAsync(scenario.RegisterId, SnapshotMonth, [new(set, new Amounts(120m, 12m).Resources)]);
        await scenario.Finalizations.MarkFinalizedAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, DateTime.UtcNow);
        await scenario.Balances.ReplaceForMonthAsync(scenario.RegisterId, september, []);
        await scenario.Finalizations.MarkFinalizedAsync(scenario.RegisterId, september, DateTime.UtcNow, DateTime.UtcNow);
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "amount")).Should().Be(0m);
        // An empty finalized snapshot is authoritative; it must not resurrect June/August.
        await scenario.AppendAsync(set, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new(7m, 0.7m));
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "amount")).Should().Be(7m);
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "quantity")).Should().Be(0.7m);
    }

    [Theory]
    [MemberData(nameof(EquivalenceCases))]
    public async Task Generated_filters_match_legacy_SQL_and_independent_decimal_model(SnapshotState state, int seed)
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString);
        var random = new Random(seed);
        // Deliberately reuse value UUIDs across different dimensions: matching values
        // without their dimension IDs must never make a set eligible.
        var values = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var combinations = Enumerable.Range(1, 242).ToArray();
        random.Shuffle(combinations);
        var models = new List<SetModel>();
        foreach (var code in combinations.Take(80).Append(242).Distinct())
        {
            var remaining = code;
            var dimensions = new List<DimensionValue>();
            foreach (var dimension in scenario.Dimensions)
            {
                var digit = remaining % 3;
                remaining /= 3;
                if (digit > 0) dimensions.Add(new(dimension, values[digit - 1]));
            }

            var id = await scenario.AddSetAsync(dimensions);
            var before = RandomAmounts(random);
            var after = RandomAmounts(random);
            var snapshot = RandomAmounts(random);
            // Include snapshot-only sets and sets with no rows in either source.
            if (models.Count % 7 == 0) before = after = new(0m, 0m);
            if (models.Count % 11 == 0) snapshot = new(0m, 0m);
            if (before != new Amounts(0m, 0m))
                await scenario.AppendAsync(id, new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc), before);
            if (after != new Amounts(0m, 0m))
            {
                // Equal-valued movements are distinct rows and must both count.
                await scenario.AppendAsync(id, DeltaDate, after);
                await scenario.AppendAsync(id, DeltaDate, after);
            }

            var cancelled = await scenario.AppendAsync(id, DeltaDate, RandomAmounts(random));
            await scenario.Movements.AppendStornoByDocumentAsync(scenario.RegisterId, cancelled);
            models.Add(new(id, dimensions.ToArray(), before, after + after, snapshot));
        }

        if (state != SnapshotState.NoTable)
        {
            await scenario.Balances.EnsureSchemaAsync(scenario.RegisterId);
            if (state != SnapshotState.EmptyTable)
                await scenario.Balances.ReplaceForMonthAsync(scenario.RegisterId, SnapshotMonth,
                    models.Where(x => x.Snapshot != new Amounts(0m, 0m))
                        .Select(x => new OperationalRegisterMonthlyProjectionRow(x.Id, x.Snapshot.Resources)).ToArray());
            if (state == SnapshotState.Finalized)
                await scenario.Finalizations.MarkFinalizedAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, DateTime.UtcNow);
            if (state == SnapshotState.Dirty)
                await scenario.Finalizations.MarkDirtyAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, DateTime.UtcNow);
            if (state == SnapshotState.Blocked)
                await scenario.Finalizations.MarkBlockedNoProjectorAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, "Test", DateTime.UtcNow);
        }

        var filters = new List<DimensionValue[]>();
        // Exhaust every non-empty subset of a five-dimensional set, in both orders.
        var full = scenario.Dimensions.Select(d => new DimensionValue(d, values[1])).ToArray();
        for (var mask = 1; mask < 32; mask++)
        {
            var subset = full.Where((_, index) => (mask & (1 << index)) != 0).ToArray();
            filters.Add(subset);
            filters.Add(subset.Reverse().ToArray());
        }
        for (var index = 0; index < 80; index++)
        {
            var order = scenario.Dimensions.ToArray();
            random.Shuffle(order);
            filters.Add(order.Take(1 + index % 5)
                .Select(d => new DimensionValue(d, values[random.Next(values.Length)])).ToArray());
        }
        filters.Add([new(scenario.Dimensions[0], Guid.NewGuid())]);
        filters.Add([new(Guid.NewGuid(), values[0])]);
        filters.Add([.. full, new(Guid.NewGuid(), values[0])]);

        foreach (var filter in filters)
        {
            var matching = models.Where(set => filter.All(pair => set.Dimensions.Contains(pair))).ToArray();
            foreach (var resource in new[] { "amount", "quantity" })
            {
                var expected = matching.Sum(set =>
                    (state == SnapshotState.Finalized ? set.Snapshot : set.Before)[resource] + set.After[resource]);
                await scenario.AssertEquivalentAsync(filter, resource, expected, state != SnapshotState.NoTable,
                    $"seed={seed}, snapshot={state}, filter={string.Join(';', filter)}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public async Task Filter_arity_and_order_preserve_exact_and_superset_matches(int dimensionCount)
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString, dimensionCount + 1);
        var filter = scenario.Dimensions.Take(dimensionCount)
            .Select(d => new DimensionValue(d, Guid.NewGuid())).ToArray();
        var exact = await scenario.AddSetAsync(filter);
        var superset = await scenario.AddSetAsync([.. filter, new(scenario.Dimensions[^1], Guid.NewGuid())]);
        var different = await scenario.AddSetAsync([new(filter[0].DimensionId, Guid.NewGuid()), .. filter.Skip(1)]);
        await scenario.AppendAsync(exact, DeltaDate, new(10.12345678m, -3.87654321m));
        await scenario.AppendAsync(superset, DeltaDate, new(20m, 5m));
        await scenario.AppendAsync(different, DeltaDate, new(1000m, 1000m));
        if (dimensionCount > 1)
        {
            var incomplete = await scenario.AddSetAsync(filter.Take(dimensionCount - 1).ToArray());
            await scenario.AppendAsync(incomplete, DeltaDate, new(2000m, 2000m));
        }

        // Exhaust all 3! / 4! orders used by payables / receivables. For larger
        // generic filters use reverse and cyclic orders to keep runtime bounded.
        var orders = dimensionCount <= 4
            ? Permutations(filter)
            : Enumerable.Range(0, dimensionCount)
                .Select(offset => filter.Skip(offset).Concat(filter.Take(offset)).ToArray())
                .Append(filter.Reverse().ToArray());
        var orderList = orders.ToArray();
        foreach (var withSnapshot in new[] { false, true })
        {
            if (withSnapshot)
            {
                await scenario.Balances.ReplaceForMonthAsync(scenario.RegisterId, SnapshotMonth,
                    [new(exact, new Amounts(100m, -20m).Resources), new(superset, new Amounts(-50m, 10m).Resources)]);
                await scenario.Finalizations.MarkFinalizedAsync(scenario.RegisterId, SnapshotMonth, DateTime.UtcNow, DateTime.UtcNow);
            }
            foreach (var order in orderList)
            {
                await scenario.AssertEquivalentAsync(order, "amount", 30.12345678m + (withSnapshot ? 50m : 0m), withSnapshot);
                await scenario.AssertEquivalentAsync(order, "quantity", 1.12345679m - (withSnapshot ? 10m : 0m), withSnapshot);
            }
        }
    }

    [Fact]
    public async Task Missing_and_empty_storage_and_resource_isolation_preserve_zero_and_exact_decimals()
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString, ensureMovements: false);
        DimensionValue[] filter = [new(scenario.Dimensions[0], Guid.NewGuid())];
        (await scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "amount")).Should().Be(0m);
        await scenario.Movements.EnsureSchemaAsync(scenario.RegisterId);
        await scenario.AssertEquivalentAsync(filter, "amount", 0m, false);
        var set = await scenario.AddSetAsync(filter);
        await scenario.AssertEquivalentAsync(filter, "amount", 0m, false);
        await scenario.AppendAsync(set, DeltaDate, new(1000000000000.12345678m, -0.00000001m));
        await scenario.AssertEquivalentAsync(filter, "amount", 1000000000000.12345678m, false);
        await scenario.AssertEquivalentAsync(filter, "quantity", -0.00000001m, false);

        // A different register can share the exact dimension set without its rows leaking in.
        var other = await scenario.AddRegisterAsync("other");
        await scenario.Movements.EnsureSchemaAsync(other);
        await scenario.Movements.AppendAsync(other, [new(Guid.NewGuid(), DeltaDate, set, new Amounts(777m, 888m).Resources)]);
        (await scenario.Reader.GetNetByDimensionsAsync(other, filter, "amount")).Should().Be(777m);
        await scenario.AssertEquivalentAsync(filter, "amount", 1000000000000.12345678m, false);
        await scenario.AssertEquivalentAsync(filter, "quantity", -0.00000001m, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_dimensions_are_rejected_without_poisoning_the_transaction(bool conflictingValue)
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString);
        var pair = new DimensionValue(scenario.Dimensions[0], Guid.NewGuid());
        var set = await scenario.AddSetAsync([pair]);
        await scenario.AppendAsync(set, DeltaDate, new(7m, 0m));
        DimensionValue[] invalid = [pair, conflictingValue ? new(pair.DimensionId, Guid.NewGuid()) : pair];
        Func<Task> read = () => scenario.Reader.GetNetByDimensionsAsync(scenario.RegisterId, invalid, "amount");
        await read.Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*duplicate dimension id*");
        await scenario.AssertEquivalentAsync([pair], "amount", 7m, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_transaction_reads_see_own_writes_but_never_uncommitted_sets_or_movements(bool commit)
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString);
        DimensionValue[] filter = [new(scenario.Dimensions[0], Guid.NewGuid()), new(scenario.Dimensions[1], Guid.NewGuid())];
        var initialSet = await scenario.AddSetAsync(filter);
        await scenario.AppendAsync(initialSet, DeltaDate, new(10m, 0m));
        await scenario.Uow.CommitAsync();

        await using var readerScope = scenario.Host.Services.CreateAsyncScope();
        var readUow = readerScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = readerScope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceNetReader>();
        await readUow.BeginTransactionAsync();
        (await reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "amount")).Should().Be(10m);
        await scenario.Uow.BeginTransactionAsync();
        await scenario.AppendAsync(initialSet, DeltaDate, new(100m, 0m));
        var newSet = await scenario.AddSetAsync([.. filter, new(scenario.Dimensions[2], Guid.NewGuid())]);
        await scenario.AppendAsync(newSet, DeltaDate, new(50m, 0m));
        await scenario.AssertEquivalentAsync(filter, "amount", 160m, false);
        (await reader.GetNetByDimensionsAsync(scenario.RegisterId, filter, "amount")).Should().Be(10m,
            "another transaction must not see uncommitted movement rows or a new matching dimension set");
        if (commit) await scenario.Uow.CommitAsync();
        else await scenario.Uow.RollbackAsync();
        (await reader.GetNetByDimensionsAsync(scenario.RegisterId, filter.Reverse().ToArray(), "amount"))
            .Should().Be(commit ? 160m : 10m, "the next READ COMMITTED statement must reflect only committed changes");
        await readUow.RollbackAsync();
        // Reuse the same writer/reader service after transaction replacement.
        await scenario.Uow.BeginTransactionAsync();
        await scenario.AssertEquivalentAsync(filter, "amount", commit ? 160m : 10m, false);
    }

    private static readonly DateOnly SnapshotMonth = new(2026, 8, 1);
    private static readonly DateTime DeltaDate = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Amounts RandomAmounts(Random random) =>
        new(random.Next(-1000000, 1000001) / 100000000m, random.Next(-10000, 10001) / 100m);

    private static IEnumerable<DimensionValue[]> Permutations(DimensionValue[] values)
    {
        if (values.Length == 0) { yield return []; yield break; }
        for (var index = 0; index < values.Length; index++)
            foreach (var tail in Permutations(values.Where((_, i) => i != index).ToArray()))
                yield return [values[index], .. tail];
    }

    private sealed record Amounts(decimal Amount, decimal Quantity)
    {
        public decimal this[string resource] => resource == "amount" ? Amount : Quantity;
        public Dictionary<string, decimal> Resources => new() { ["amount"] = Amount, ["quantity"] = Quantity };
        public static Amounts operator +(Amounts left, Amounts right) => new(left.Amount + right.Amount, left.Quantity + right.Quantity);
    }

    private sealed record SetModel(Guid Id, DimensionValue[] Dimensions, Amounts Before, Amounts After, Amounts Snapshot);

    private sealed class Scenario(IHost host, AsyncServiceScope scope) : IAsyncDisposable
    {
        public IHost Host => host;
        public IServiceProvider Services => scope.ServiceProvider;
        public IUnitOfWork Uow => Services.GetRequiredService<IUnitOfWork>();
        public IOperationalRegisterResourceNetReader Reader => Services.GetRequiredService<IOperationalRegisterResourceNetReader>();
        public IOperationalRegisterMovementsStore Movements => Services.GetRequiredService<IOperationalRegisterMovementsStore>();
        public IOperationalRegisterBalancesStore Balances => Services.GetRequiredService<IOperationalRegisterBalancesStore>();
        public IOperationalRegisterFinalizationRepository Finalizations => Services.GetRequiredService<IOperationalRegisterFinalizationRepository>();
        public Guid RegisterId { get; private set; }
        public Guid[] Dimensions { get; private set; } = [];
        private string TableCode { get; set; } = "";

        public static async Task<Scenario> CreateAsync(string connectionString, int dimensionCount = 5, bool ensureMovements = true)
        {
            var host = IntegrationHostFactory.Create(connectionString);
            var scenario = new Scenario(host, host.Services.CreateAsyncScope());
            try
            {
                await scenario.Uow.BeginTransactionAsync();
                scenario.RegisterId = await scenario.AddRegisterAsync("main");
                scenario.TableCode = (await scenario.Services.GetRequiredService<IOperationalRegisterRepository>()
                    .GetByIdAsync(scenario.RegisterId))!.TableCode;
                scenario.Dimensions = Enumerable.Range(0, dimensionCount).Select(_ => Guid.NewGuid()).ToArray();
                await scenario.Uow.Connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO platform_dimensions(dimension_id,code,name) VALUES (@Id,@Code,@Code)",
                    scenario.Dimensions.Select(id => new { Id = id, Code = $"eq_{id:N}" }), scenario.Uow.Transaction));
                if (ensureMovements) await scenario.Movements.EnsureSchemaAsync(scenario.RegisterId);
                return scenario;
            }
            catch { await scenario.DisposeAsync(); throw; }
        }

        public async Task<Guid> AddRegisterAsync(string suffix)
        {
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await Services.GetRequiredService<IOperationalRegisterRepository>().UpsertAsync(
                new OperationalRegisterUpsert(id, $"eq_{suffix}_{id:N}", "Equivalence test"), now);
            await Services.GetRequiredService<IOperationalRegisterResourceRepository>().ReplaceAsync(id,
                [new OperationalRegisterResourceDefinition("amount", "Amount", 1), new OperationalRegisterResourceDefinition("quantity", "Quantity", 2)], now);
            return id;
        }

        public Task<Guid> AddSetAsync(IReadOnlyList<DimensionValue> filter) =>
            Services.GetRequiredService<IDimensionSetService>().GetOrCreateIdAsync(new DimensionBag(filter));

        public async Task<Guid> AppendAsync(Guid set, DateTime date, Amounts amounts)
        {
            var document = Guid.NewGuid();
            await Movements.AppendAsync(RegisterId, [new(document, date, set, amounts.Resources)]);
            return document;
        }

        public async Task AssertEquivalentAsync(DimensionValue[] filter, string resource, decimal expected, bool balancesTable, string reason = "")
        {
            var actual = await Reader.GetNetByDimensionsAsync(RegisterId, filter, resource);
            actual.Should().Be(expected, "the optimized reader must preserve the contract. {0}", reason);
            // Compatibility is supplementary: actual always passes an independently
            // supplied expectation before the old query is even executed.
            var legacy = await LegacyNetAsync(filter, resource, balancesTable);
            legacy.Should().Be(expected, "the previous SQL must also agree with the independent expectation. {0}", reason);
        }

        // Frozen pre-optimization SQL, deliberately independent of production SQL
        // builders (including snapshot selection). Keep this as a differential oracle.
        private Task<decimal> LegacyNetAsync(DimensionValue[] filter, string resource, bool balancesTable)
        {
            if (resource is not ("amount" or "quantity")) throw new ArgumentOutOfRangeException(nameof(resource));
            var matching = """
                SELECT item.dimension_set_id
                FROM platform_dimension_set_items item
                JOIN UNNEST(@DimensionIds::uuid[], @ValueIds::uuid[]) AS requested(dimension_id,value_id)
                  ON requested.dimension_id=item.dimension_id AND requested.value_id=item.value_id
                GROUP BY item.dimension_set_id
                HAVING COUNT(*)=@DimensionCount
                """;
            var sql = balancesTable ? $"""
                WITH matching_dimension_sets AS ({matching}),
                latest_snapshot AS (
                    SELECT MAX(finalized.period) AS period_month
                    FROM operational_register_finalizations finalized
                    WHERE finalized.register_id=@RegisterId AND finalized.status=1
                      AND NOT EXISTS (
                        SELECT 1 FROM operational_register_finalizations invalidated
                        WHERE invalidated.register_id=finalized.register_id
                          AND invalidated.period<=finalized.period AND invalidated.status<>1)
                ), snapshot AS (
                    SELECT COALESCE(SUM(balance.{resource}),0) AS amount
                    FROM matching_dimension_sets matching CROSS JOIN latest_snapshot latest
                    JOIN opreg_{TableCode}__balances balance
                      ON balance.period_month=latest.period_month AND balance.dimension_set_id=matching.dimension_set_id
                ), delta AS (
                    SELECT COALESCE(SUM(CASE WHEN movement.is_storno THEN -movement.{resource} ELSE movement.{resource} END),0) AS amount
                    FROM matching_dimension_sets matching CROSS JOIN latest_snapshot latest
                    JOIN opreg_{TableCode}__movements movement ON movement.dimension_set_id=matching.dimension_set_id
                      AND (latest.period_month IS NULL OR movement.period_month>latest.period_month)
                ) SELECT snapshot.amount+delta.amount FROM snapshot CROSS JOIN delta;
                """ : $"""
                WITH matching_dimension_sets AS ({matching})
                SELECT COALESCE(SUM(CASE WHEN movement.is_storno THEN -movement.{resource} ELSE movement.{resource} END),0)
                FROM opreg_{TableCode}__movements movement
                WHERE movement.dimension_set_id IN (SELECT dimension_set_id FROM matching_dimension_sets);
                """;
            return Uow.Connection.ExecuteScalarAsync<decimal>(new CommandDefinition(sql,
                new { RegisterId, DimensionIds = filter.Select(x => x.DimensionId).ToArray(),
                    ValueIds = filter.Select(x => x.ValueId).ToArray(), DimensionCount = filter.Length }, Uow.Transaction));
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            host.Dispose();
        }
    }
}
