using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(RegistersPostgresCollection.Name)]
public sealed class OperationalRegisterStornoHistoryTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task NormalDocumentPostUnpostCycles_GrowLinearly_AndRetriesRemainNoOps()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, RegisterDocument>();
            services.AddScoped<RegisterPosting>();
        });
        await using var setup = host.Services.CreateAsyncScope();
        var management = setup.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var registerId = await management.UpsertAsync("proof_cycles", "Normal lifecycle proof");
        await management.ReplaceResourcesAsync(registerId, [new("amount", "Amount", 1)]);
        var date = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var id = await setup.ServiceProvider.GetRequiredService<IDocumentDraftService>()
            .CreateDraftAsync("proof_opreg", "PROOF-CYCLES", date);
        var counts = new List<int>();

        for (var cycle = 0; cycle < 4; cycle++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(id);
            await AssertProjectionsAsync(scope.ServiceProvider, registerId, 100m);
            await posting.UnpostAsync(id);
            await AssertProjectionsAsync(scope.ServiceProvider, registerId, 0m);
            var rows = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsReader>()
                .GetByMonthAsync(registerId, new DateOnly(2026, 1, 1));
            counts.Add(rows.Count);
            rows.Sum(row => (row.IsStorno ? -1m : 1m) * row.Resources["amount"]).Should().Be(0m);

            await posting.UnpostAsync(id);
            var repeated = await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsReader>()
                .GetByMonthAsync(registerId, new DateOnly(2026, 1, 1));
            repeated.Should().HaveCount(rows.Count, "retrying an already completed Unpost remains a no-op");
        }

        counts.Should().Equal(2, 4, 6, 8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyHistory_IsPreserved_AndOnlyUnpairedMovementsAreCancelled(bool withResources)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var registerId = await management.UpsertAsync("proof_legacy", "Legacy compatibility proof");
        await management.ReplaceResourcesAsync(registerId, withResources
            ? [new("amount", "Amount", 1), new("quantity", "Quantity", 2)]
            : []);
        var date = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var documentId = await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>()
            .CreateDraftAsync("test_doc", "PROOF-LEGACY", date);
        var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
        var reader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsReader>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dimensionId = Guid.NewGuid();
        await uow.BeginTransactionAsync();
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@dimensionId, 'storno_test', 'Storno test');",
            new { dimensionId }, transaction: uow.Transaction));
        var dimensionSetId = await scope.ServiceProvider.GetRequiredService<IDimensionSetService>()
            .GetOrCreateIdAsync(new DimensionBag([new DimensionValue(dimensionId, Guid.NewGuid())]));
        await uow.CommitAsync();

        OperationalRegisterMovement Movement(DateTime occurredAt, decimal amount, decimal quantity, Guid dimensions = default)
            => new(documentId, occurredAt, dimensions,
                withResources ? new Dictionary<string, decimal> { ["amount"] = amount, ["quantity"] = quantity }
                    : new Dictionary<string, decimal>());

        // Identical rows remain separate business movements. Negative and zero resources are legal.
        OperationalRegisterMovement[] batch =
        [
            Movement(date, 100m, 2m),
            Movement(date, 100m, 2m),
            Movement(date, -100m, -2m),
            Movement(date.AddMonths(1), 0m, 0m),
            Movement(date, 100m, 3m),
            Movement(date.AddHours(1), 100m, 2m),
            Movement(date, 100m, 2m, dimensionSetId)
        ];
        var register = (await scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>()
            .GetByIdAsync(registerId))!;
        var table = $"opreg_{register.TableCode}__movements";
        var resourceColumns = withResources ? ", amount, quantity" : string.Empty;
        var counts = new List<int>();
        for (var cycle = 0; cycle < 4; cycle++)
        {
            await uow.BeginTransactionAsync();
            await store.EnsureSchemaAsync(registerId);
            await store.AppendAsync(registerId, batch);
            // Arrange the old algorithm explicitly, so this remains a legacy-data test after the fix.
            await uow.Connection.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO {table} (document_id, occurred_at_utc, dimension_set_id, is_storno{resourceColumns})
                SELECT document_id, occurred_at_utc, dimension_set_id, NOT is_storno{resourceColumns}
                FROM {table} WHERE document_id = @documentId;
                """, new { documentId }, transaction: uow.Transaction));
            await uow.CommitAsync();
            counts.Add((await ReadHistory()).Length);
        }
        counts.Should().Equal(14, 42, 98, 210);

        await uow.BeginTransactionAsync();
        await store.AppendAsync(registerId, batch);
        await uow.CommitAsync();
        var history = await ReadHistory();
        history.Should().HaveCount(217);
        var originalIds = history.Select(row => row.MovementId).ToArray();

        await uow.BeginTransactionAsync();
        await store.AppendStornoByDocumentAsync(registerId, documentId);
        (await ReadHistory()).Should().HaveCount(224);
        await uow.RollbackAsync();
        (await ReadHistory()).Should().BeEquivalentTo(history, options => options.WithStrictOrdering());

        await uow.BeginTransactionAsync();
        await store.AppendStornoByDocumentAsync(registerId, documentId);
        await uow.CommitAsync();
        var cancelled = await ReadHistory();
        cancelled.Should().HaveCount(224);
        cancelled.Take(history.Length).Should().BeEquivalentTo(history, options => options.WithStrictOrdering());
        cancelled.Skip(history.Length).Select(row => row.MovementId).Should().OnlyContain(id => id > originalIds.Max());
        foreach (var group in cancelled.GroupBy(row => (row.DocumentId, row.OccurredAtUtc, row.DimensionSetId)))
        {
            group.Sum(row => row.IsStorno ? -1 : 1).Should().Be(0);
            foreach (var resource in withResources ? new[] { "amount", "quantity" } : [])
                group.Sum(row => (row.IsStorno ? -1m : 1m) * row.Resources[resource]).Should().Be(0m);
        }

        await uow.BeginTransactionAsync();
        await store.AppendStornoByDocumentAsync(registerId, documentId);
        await uow.CommitAsync();
        (await ReadHistory()).Should().BeEquivalentTo(cancelled, options => options.WithStrictOrdering());

        async Task<OperationalRegisterMovementRead[]> ReadHistory()
            => (await reader.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1)))
                .Concat(await reader.GetByMonthAsync(registerId, new DateOnly(2026, 2, 1)))
                .OrderBy(row => row.MovementId)
                .ToArray();
    }

    [Fact]
    public async Task ConcurrentStorno_AppendsOnlyOneCancellation()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var setup = host.Services.CreateAsyncScope();
        var registerId = await setup.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>()
            .UpsertAsync("storno_race", "Storno race");
        var uow = setup.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var store = setup.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
        var documentId = Guid.NewGuid();
        await uow.BeginTransactionAsync();
        await store.EnsureSchemaAsync(registerId);
        await store.AppendAsync(registerId,
            [new(documentId, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), Guid.Empty, new Dictionary<string, decimal>())]);
        await uow.CommitAsync();

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var participants = 0;
        async Task CancelAsync()
        {
            await using var scope = host.Services.CreateAsyncScope();
            var transaction = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await transaction.BeginTransactionAsync();
            if (Interlocked.Increment(ref participants) == 2)
                ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>()
                .AppendStornoByDocumentAsync(registerId, documentId);
            await transaction.CommitAsync();
        }

        await Task.WhenAll(CancelAsync(), CancelAsync()).WaitAsync(TimeSpan.FromSeconds(15));
        var rows = await setup.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsReader>()
            .GetByMonthAsync(registerId, new DateOnly(2026, 1, 1));
        rows.Should().HaveCount(2);
        rows.Count(row => row.IsStorno).Should().Be(1);
    }

    private static async Task AssertProjectionsAsync(IServiceProvider services, Guid registerId, decimal expected)
    {
        var month = new DateOnly(2026, 1, 1);
        var uow = services.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await services.GetRequiredService<IOperationalRegisterDefaultProjectionRebuilder>()
            .RebuildMonthAsync(registerId, month, null);
        await uow.CommitAsync();
        var turnovers = await services.GetRequiredService<IOperationalRegisterTurnoversReader>()
            .GetByMonthsAsync(registerId, month, month);
        var balances = await services.GetRequiredService<IOperationalRegisterBalancesReader>()
            .GetByMonthsAsync(registerId, month, month);
        turnovers.Sum(row => row.Values["amount"]).Should().Be(expected);
        balances.Sum(row => row.Values["amount"]).Should().Be(expected);
    }

    private sealed class RegisterDocument : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("proof_opreg", document => document
                .Metadata(new DocumentTypeMetadata("proof_opreg", [],
                    new DocumentPresentationMetadata("Register cycle proof"),
                    new DocumentMetadataVersion(1, "proof")))
                .OperationalRegisterPostingHandler<RegisterPosting>());
        }
    }

    private sealed class RegisterPosting : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => "proof_opreg";

        public Task BuildMovementsAsync(DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct)
        {
            builder.Add("proof_cycles", new OperationalRegisterMovement(document.Id, document.DateUtc, Guid.Empty,
                new Dictionary<string, decimal> { ["amount"] = 100m }));
            return Task.CompletedTask;
        }
    }
}
