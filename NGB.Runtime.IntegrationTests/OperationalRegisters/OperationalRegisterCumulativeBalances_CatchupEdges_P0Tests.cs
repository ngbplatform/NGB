using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

public sealed partial class OperationalRegisterCumulativeBalances_DefaultProjector_P0Tests
{
    [Theory]
    [InlineData(1729)]
    [InlineData(4099)]
    [InlineData(20260927)]
    public async Task Random_signed_multi_resource_ledger_matches_independent_model_across_prefix_and_tail(int seed)
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var id = Guid.CreateVersion7();
        await SeedRegisterAsync(host, id, "it_model_" + id.ToString("N"),
            new[] { new OperationalRegisterResourceDefinition("amount", "Amount", 1), new OperationalRegisterResourceDefinition("quantity", "Quantity", 2) });
        Guid dimensionSet;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            var dimension = Guid.CreateVersion7();
            await uow.Connection.ExecuteAsync("INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, 'Model dimension')",
                new { Id = dimension, Code = "dim_" + dimension.ToString("N") }, transaction: uow.Transaction);
            dimensionSet = await scope.ServiceProvider.GetRequiredService<IDimensionSetService>()
                .GetOrCreateIdAsync(new DimensionBag(new[] { new DimensionValue(dimension, Guid.CreateVersion7()) }));
            await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>().EnsureSchemaAsync(id);
            await uow.CommitAsync();
        }
        var random = new Random(seed);
        var ledger = new List<OperationalRegisterMovement>();
        for (var index = 0; index < 80; index++)
            ledger.Add(new OperationalRegisterMovement(Guid.CreateVersion7(), JanuaryDate.AddMonths(random.Next(-1, 2)),
                index < 40 ? Guid.Empty : dimensionSet,
                new Dictionary<string, decimal> { ["amount"] = random.Next(-1000, 1001) / 8m, ["quantity"] = random.Next(-80, 81) / 16m }));
        await AppendBatchAsync(host, id, ledger.Take(40).ToArray());
        await MarkMonthDirtyAsync(host, id, January);
        gate.Arm();
        var running = FinalizeDirtyAsync(host, id);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await AppendBatchAsync(host, id, ledger.Skip(40).ToArray());
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
            foreach (var original in ledger.Where((_, index) => index % 7 == 0).ToArray())
            {
                await store.AppendStornoByDocumentAsync(id, original.DocumentId);
                ledger.Add(original with { Resources = original.Resources.ToDictionary(kv => kv.Key, kv => -kv.Value) });
            }
            await uow.CommitAsync();
        }
        finally { gate.Release.TrySetResult(); }
        (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
        await using var check = host.Services.CreateAsyncScope();
        foreach (var cumulative in new[] { false, true })
        {
            var actual = cumulative
                ? await check.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>().GetByMonthAsync(id, January)
                : await check.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>().GetByMonthAsync(id, January);
            var expected = ledger.Where(row => cumulative ? row.OccurredAtUtc < new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) : row.OccurredAtUtc.Year == 2026 && row.OccurredAtUtc.Month == 1)
                .GroupBy(row => row.DimensionSetId)
                .Where(group => group.Sum(row => row.Resources["amount"]) != 0m || group.Sum(row => row.Resources["quantity"]) != 0m)
                .ToDictionary(group => group.Key, group => new Dictionary<string, decimal>
                { ["amount"] = group.Sum(row => row.Resources["amount"]), ["quantity"] = group.Sum(row => row.Resources["quantity"]) });
            actual.ToDictionary(row => row.DimensionSetId, row => row.Values).Should().BeEquivalentTo(expected);
        }
    }

    [Fact]
    public async Task Failure_after_turnover_catchup_rolls_back_both_projections_and_dirty_status()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (id, _) = await SeedProjectionAsync(host);
        await MarkMonthDirtyAsync(host, id, January);
        var document = Guid.CreateVersion7();
        await SeedDocumentAsync(host, document, JanuaryDate);
        await using var admin = host.Services.CreateAsyncScope();
        var register = await admin.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>().GetByIdAsync(id);
        var uow = admin.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var table = OperationalRegisterNaming.BalancesTable(register!.TableCode);
        await uow.EnsureConnectionOpenAsync();
        await uow.Connection.ExecuteAsync($"""
CREATE FUNCTION it_reject_tail() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN IF NEW.amount = 120 THEN RAISE EXCEPTION 'Injected tail failure'; END IF; RETURN NEW; END; $$;
CREATE TRIGGER it_reject_tail BEFORE INSERT OR UPDATE ON {table} FOR EACH ROW EXECUTE FUNCTION it_reject_tail();
""");
        gate.Arm();
        var running = FinalizeDirtyAsync(host, id);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await ApplyPostAsync(host, id, document, CreateMovement(document, JanuaryDate, 20m));
            gate.Release.TrySetResult();
            await ((Func<Task>)(async () => await running.WaitAsync(TimeSpan.FromSeconds(10))))
                .Should().ThrowAsync<PostgresException>();
            (await ReadBalanceAsync(host, id)).Should().Be(100m);
            (await admin.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>().GetByMonthAsync(id, January))
                .Single().Values["amount"].Should().Be(100m);
            (await admin.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>().GetAsync(id, January))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
        }
        finally
        {
            gate.Release.TrySetResult();
            await uow.Connection.ExecuteAsync($"DROP TRIGGER it_reject_tail ON {table}; DROP FUNCTION it_reject_tail();");
        }
        (await FinalizeDirtyAsync(host, id)).Should().Be(1);
        (await ReadBalanceAsync(host, id)).Should().Be(120m);
    }

    [Fact]
    public async Task Prepared_result_rejects_reuse_and_use_after_transaction_end()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (id, _) = await SeedProjectionAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var rebuilder = (IOperationalRegisterProjectionPreparation)scope.ServiceProvider.GetRequiredService<IOperationalRegisterDefaultProjectionRebuilder>();
        var locks = (NGB.Persistence.Locks.IOperationalRegisterFinalizationLockManager)scope.ServiceProvider.GetRequiredService<NGB.Persistence.Locks.IAdvisoryLockManager>();
        await uow.BeginTransactionAsync();
        await locks.LockOperationalRegisterFinalizationAsync(id);
        var prepared = await rebuilder.PrepareMonthAsync(id, January);
        await prepared.CompleteAsync();
        await ((Func<Task>)(() => prepared.CompleteAsync())).Should().ThrowAsync<NgbInvariantViolationException>();
        await uow.RollbackAsync();
        await uow.BeginTransactionAsync();
        await locks.LockOperationalRegisterFinalizationAsync(id);
        var expired = await rebuilder.PrepareMonthAsync(id, January);
        await uow.RollbackAsync();
        await ((Func<Task>)(() => expired.CompleteAsync())).Should().ThrowAsync<NgbInvariantViolationException>();
    }

    [Fact]
    public async Task Zero_resource_register_preserves_dimension_presence_during_catchup()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var id = Guid.CreateVersion7();
        await SeedRegisterAsync(host, id, "it_empty_" + id.ToString("N"), []);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>().EnsureSchemaAsync(id);
            await uow.CommitAsync();
        }
        OperationalRegisterMovement row = new(Guid.CreateVersion7(), JanuaryDate, Guid.Empty, new Dictionary<string, decimal>());
        await AppendBatchAsync(host, id, new[] { row });
        await MarkMonthDirtyAsync(host, id, January);
        gate.Arm();
        var running = FinalizeDirtyAsync(host, id);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await AppendBatchAsync(host, id, new[] { row with { DocumentId = Guid.CreateVersion7() } }); }
        finally { gate.Release.TrySetResult(); }
        (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
        await using var check = host.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>().GetByMonthAsync(id, January))
            .Should().ContainSingle().Which.DimensionSetId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task Publication_wait_participates_in_database_deadlock_detection_with_external_transactions()
    {
        var gate = new PreparationGate();
        using var host = GatedHost(gate);
        var (id, document) = await SeedProjectionAsync(host);
        await ApplyRepostAsync(host, id, document, CreateMovement(document, JanuaryDate, 120m));
        gate.Arm();
        var running = FinalizeDirtyAsync(host, id);
        await gate.Prepared.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var external = host.Services.CreateAsyncScope();
        var uow = external.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await external.ServiceProvider.GetRequiredService<NGB.Persistence.Locks.IAdvisoryLockManager>()
            .LockOperationalRegisterAsync(id);
        var register = await external.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>().GetByIdAsync(id);
        // This session checks the cycle first. It holds ORR and waits for the prepared row;
        // the finalizer holds that row and queues for ORR. Try-lock polling would hide the cycle.
        await uow.Connection.ExecuteAsync("SET LOCAL deadlock_timeout = '50ms'", transaction: uow.Transaction);
        var update = uow.Connection.ExecuteAsync($"UPDATE {OperationalRegisterNaming.BalancesTable(register!.TableCode)} SET amount = amount + 1 WHERE period_month = @Month",
            new { Month = January }, transaction: uow.Transaction);
        try
        {
            gate.Release.TrySetResult();
            await gate.Completing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var failure = await ((Func<Task>)(async () => await update.WaitAsync(TimeSpan.FromSeconds(4))))
                .Should().ThrowAsync<PostgresException>();
            failure.Which.SqlState.Should().Be("40P01");
        }
        finally { await uow.RollbackAsync(); gate.Release.TrySetResult(); }
        (await running.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(1);
        (await ReadBalanceAsync(host, id)).Should().Be(120m);
    }

    private static async Task AppendBatchAsync(Microsoft.Extensions.Hosting.IHost host, Guid id, IReadOnlyList<OperationalRegisterMovement> movements)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>().AppendAsync(id, movements);
        await uow.CommitAsync();
    }
}
