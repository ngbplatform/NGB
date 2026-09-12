using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(RegistersPostgresCollection.Name)]
public sealed class OperationalRegisterResourceNets_StaleSnapshots_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backdated_post_repost_and_unpost_are_visible_before_projection_rebuild(bool hasEarlierCleanSnapshot)
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString);
        if (hasEarlierCleanSnapshot)
            await scenario.PostItemAsync(new DateOnly(2026, 8, 1), 100m);
        await scenario.PostItemAsync(new DateOnly(2027, 1, 1), 10m);
        await scenario.FinalizeAsync();

        var itemId = await scenario.PostItemAsync(new DateOnly(2026, 9, 1), 555m);
        var state = await scenario.Services.GetRequiredService<IOperationalRegisterFinalizationRepository>()
            .GetAsync(scenario.RegisterId, new DateOnly(2027, 1, 1), default);
        state!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

        await scenario.AssertAllReadersAsync(itemId, 555m);
        await scenario.FinalizeAsync();
        await scenario.AssertAllReadersAsync(itemId, 555m);

        var offsetItemId = await scenario.PostItemAsync(new DateOnly(2026, 9, 1), -555m);
        await scenario.AssertAllReadersAsync(offsetItemId, -555m);
        await scenario.FinalizeAsync();

        var transferId = await scenario.PostAsync(new DateOnly(2026, 9, 1), (itemId, -555m), (offsetItemId, 555m));
        await scenario.AssertAllReadersAsync(itemId, 0m);
        await scenario.AssertAllReadersAsync(offsetItemId, 0m);
        await scenario.FinalizeAsync();
        await scenario.AssertAllReadersAsync(itemId, 0m);

        await scenario.UnpostAsync(transferId);
        await scenario.AssertAllReadersAsync(itemId, 555m);
        await scenario.AssertAllReadersAsync(offsetItemId, -555m);
        await scenario.RepostAsync(itemId);
        await scenario.AssertAllReadersAsync(itemId, 555m);
        await scenario.UnpostAsync(itemId);
        await scenario.AssertAllReadersAsync(itemId, 0m);
    }

    [Fact]
    public async Task Empty_finalized_snapshot_and_point_in_time_boundaries_preserve_new_movements()
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString);
        var itemId = await scenario.PostItemAsync(new DateOnly(2026, 9, 1), 555m);
        await scenario.UnpostAsync(itemId);
        await scenario.FinalizeAsync();
        var snapshots = await scenario.Services.GetRequiredService<IOperationalRegisterBalancesStore>()
            .GetByMonthAsync(scenario.RegisterId, new DateOnly(2026, 9, 1), ct: default);
        snapshots.Should().BeEmpty("zero cumulative balances are pruned");

        var nextItem = await scenario.PostItemAsync(new DateOnly(2026, 9, 15), 123m);
        await scenario.AssertAllReadersAsync(nextItem, 123m);
        await scenario.AssertPointInTimeAsync(nextItem, new DateOnly(2026, 9, 14), 0m);
        await scenario.AssertPointInTimeAsync(nextItem, new DateOnly(2026, 9, 15), 123m);
        await scenario.FinalizeAsync();
        await scenario.AssertAllReadersAsync(nextItem, 123m);
        await scenario.AssertPointInTimeAsync(nextItem, new DateOnly(2026, 9, 14), 0m);
        await scenario.AssertPointInTimeAsync(nextItem, new DateOnly(2026, 9, 15), 123m);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Later_finalized_marker_cannot_hide_an_earlier_dirty_or_blocked_period(bool blocked)
    {
        await using var scenario = await Scenario.CreateAsync(Fixture.ConnectionString);
        await scenario.PostItemAsync(new DateOnly(2027, 1, 1), 10m);
        await scenario.FinalizeAsync();
        var itemId = await scenario.PostItemAsync(new DateOnly(2026, 9, 1), 555m);

        await scenario.InTransactionAsync(async () =>
        {
            var finalizations = scenario.Services.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            var now = DateTime.UtcNow;
            if (blocked)
                await finalizations.MarkBlockedNoProjectorAsync(scenario.RegisterId, new DateOnly(2026, 9, 1), now, "Test blocked rebuild", now);
            // Simulate a later period marked finalized without rebuilding its invalid baseline.
            await finalizations.MarkFinalizedAsync(scenario.RegisterId, new DateOnly(2027, 1, 1), now, now);
        });

        await scenario.AssertAllReadersAsync(itemId, 555m);
    }

    private sealed class Scenario(IHost host, AsyncServiceScope scope) : IAsyncDisposable
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        private Guid ItemDimension { get; } = Guid.CreateVersion7();
        private Guid ScopeDimension { get; } = Guid.CreateVersion7();
        private Guid ScopeId { get; } = Guid.CreateVersion7();
        private readonly Dictionary<Guid, IReadOnlyList<OperationalRegisterMovement>> _postings = new();

        public static async Task<Scenario> CreateAsync(string connectionString)
        {
            var host = IntegrationHostFactory.Create(connectionString);
            var scenario = new Scenario(host, host.Services.CreateAsyncScope());
            await scenario.InTransactionAsync(async () =>
            {
                var services = scenario.Services;
                var now = DateTime.UtcNow;
                await services.GetRequiredService<IOperationalRegisterRepository>().UpsertAsync(
                    new OperationalRegisterUpsert(scenario.RegisterId, "it_snapshot_register", "Snapshot test register"), now, default);
                await services.GetRequiredService<IOperationalRegisterResourceRepository>().ReplaceAsync(
                    scenario.RegisterId, [new OperationalRegisterResourceDefinition("amount", "Amount", 1)], now, default);

                var uow = services.GetRequiredService<IUnitOfWork>();
                await uow.Connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO platform_dimensions (dimension_id, code, name)
                    VALUES (@Id, @Code, @Name);
                    """,
                    new[]
                    {
                        new { Id = scenario.ItemDimension, Code = "it_snapshot_item", Name = "Test item" },
                        new { Id = scenario.ScopeDimension, Code = "it_snapshot_scope", Name = "Test scope" }
                    }, transaction: uow.Transaction));
                await services.GetRequiredService<IOperationalRegisterDimensionRuleRepository>().ReplaceAsync(
                    scenario.RegisterId,
                    [
                        new OperationalRegisterDimensionRule(scenario.ItemDimension, "it_snapshot_item", Ordinal: 1, IsRequired: true),
                        new OperationalRegisterDimensionRule(scenario.ScopeDimension, "it_snapshot_scope", Ordinal: 2, IsRequired: true)
                    ], now, default);
            });
            return scenario;
        }

        private DimensionValue[] Dimensions(Guid itemId) => [new(ItemDimension, itemId), new(ScopeDimension, ScopeId)];

        public Task<Guid> PostItemAsync(DateOnly date, decimal amount)
        {
            var documentId = Guid.CreateVersion7();
            return CreateAndPostAsync(documentId, date, [(documentId, amount)]);
        }

        public Task<Guid> PostAsync(DateOnly date, params (Guid ItemId, decimal Amount)[] amounts)
            => CreateAndPostAsync(Guid.CreateVersion7(), date, amounts);

        private async Task<Guid> CreateAndPostAsync(Guid documentId, DateOnly date, (Guid ItemId, decimal Amount)[] amounts)
        {
            var occurredAtUtc = date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
            var movements = new List<OperationalRegisterMovement>();
            await InTransactionAsync(async () =>
            {
                await Services.GetRequiredService<IDocumentRepository>().CreateAsync(new DocumentRecord
                {
                    Id = documentId,
                    TypeCode = "it_doc",
                    Number = "IT-" + documentId.ToString("N")[^12..],
                    DateUtc = occurredAtUtc,
                    Status = DocumentStatus.Draft,
                    CreatedAtUtc = occurredAtUtc,
                    UpdatedAtUtc = occurredAtUtc
                }, default);
                foreach (var (itemId, amount) in amounts)
                {
                    var setId = await Services.GetRequiredService<IDimensionSetService>()
                        .GetOrCreateIdAsync(new DimensionBag(Dimensions(itemId)), default);
                    movements.Add(new OperationalRegisterMovement(documentId, occurredAtUtc, setId,
                        new Dictionary<string, decimal> { ["amount"] = amount }));
                }
            });
            await ApplyAsync(documentId, OperationalRegisterWriteOperation.Post, movements);
            _postings.Add(documentId, movements);
            return documentId;
        }

        public Task UnpostAsync(Guid documentId) => ApplyAsync(documentId, OperationalRegisterWriteOperation.Unpost, []);
        public Task RepostAsync(Guid documentId) => ApplyAsync(documentId, OperationalRegisterWriteOperation.Repost, _postings[documentId]);

        private async Task ApplyAsync(Guid documentId, OperationalRegisterWriteOperation operation, IReadOnlyList<OperationalRegisterMovement> movements)
            => (await Services.GetRequiredService<IOperationalRegisterMovementsApplier>()
                .ApplyMovementsForDocumentAsync(RegisterId, documentId, operation, movements, ct: default))
                .Should().Be(OperationalRegisterWriteResult.Executed);

        public Task<int> FinalizeAsync() => Services.GetRequiredService<IOperationalRegisterFinalizationRunner>()
            .FinalizeRegisterDirtyAsync(RegisterId, maxPeriods: 20, ct: default);

        public Task AssertPointInTimeAsync(Guid itemId, DateOnly asOf, decimal expected) => InTransactionAsync(async () =>
        {
            var nets = Services.GetRequiredService<IOperationalRegisterResourceNetReader>();
            (await nets.GetNetsByDimensionsAsync(RegisterId,
                [Dimensions(Guid.NewGuid()), Dimensions(itemId)], "amount", asOf))
                .Should().Equal(0m, expected);
        });

        public Task AssertAllReadersAsync(Guid itemId, decimal expected) => InTransactionAsync(async () =>
        {
            var dimensions = Dimensions(itemId);
            var setId = await Services.GetRequiredService<IDimensionSetService>().GetOrCreateIdAsync(new DimensionBag(dimensions), default);
            var nets = Services.GetRequiredService<IOperationalRegisterResourceNetReader>();
            (await nets.GetNetByDimensionSetAsync(RegisterId, setId, "amount")).Should().Be(expected);
            (await nets.GetNetByDimensionSetsAsync(RegisterId, [setId], "amount"))[setId].Should().Be(expected);
            (await nets.GetNetByDimensionsAsync(RegisterId, dimensions, "amount")).Should().Be(expected);
            (await nets.GetNetsByDimensionsAsync(RegisterId, [dimensions], "amount", DateOnly.MaxValue)).Should().Equal(expected);
            (await nets.GetNetsByDimensionsAsync(RegisterId, [dimensions], "amount", new DateOnly(2026, 8, 31))).Should().Equal(0m);
            (await nets.GetNetsByDimensionsAsync(RegisterId, [dimensions], "amount", new DateOnly(2026, 9, 30))).Should().Equal(expected);
            var query = Services.GetRequiredService<IOperationalRegisterMovementsQueryReader>();
            var page = await query.GetResourceBalancesByDimensionPageAsync(RegisterId, new DateOnly(2027, 1, 1), dimensions, ItemDimension, "amount", 0, 10);
            page.Rows.Sum(x => x.NetAmount).Should().Be(expected);
            page.Total.Should().Be(expected == 0 ? 0 : 1);
            var cursor = await query.GetResourceBalancesByDimensionCursorAsync(RegisterId, new DateOnly(2026, 9, 1), dimensions, ItemDimension, "amount", null, 10);
            cursor.Rows.Sum(x => x.NetAmount).Should().Be(expected);
            cursor.Total.Should().Be(expected == 0 ? 0 : 1);
        });

        public async Task InTransactionAsync(Func<Task> action)
        {
            var uow = Services.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync(default);
            try
            {
                await action();
                await uow.CommitAsync(default);
            }
            catch
            {
                await uow.RollbackAsync(default);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            host.Dispose();
        }
    }
}
