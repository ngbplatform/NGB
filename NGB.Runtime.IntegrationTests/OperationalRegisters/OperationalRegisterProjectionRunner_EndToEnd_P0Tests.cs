using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Runtime.OperationalRegisters.Projections.Examples;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterProjectionRunner_EndToEnd_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Runner_RebuildsTurnoversAndBalances_FromMovements_UsingMovementsCountProjector()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<IOperationalRegisterMonthProjector>(sp =>
                    new MovementsCountProjector(
                        registerCodeNorm: "rr",
                        turnovers: sp.GetRequiredService<IOperationalRegisterTurnoversStore>(),
                        balances: sp.GetRequiredService<IOperationalRegisterBalancesStore>()));
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll", resources: new[]
        {
            new OperationalRegisterResourceDefinition("movement_count","Movement Count", 1)
        });

        var nonEmptySetId = await CreateNonEmptyDimensionSetIdAndAllowInRegisterAsync(host, registerId);

        var documentId = Guid.CreateVersion7();
        await SeedDocumentAsync(host, documentId);
        var jan10 = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        var movements = new[]
        {
            new OperationalRegisterMovement(documentId, jan10.AddDays(0), Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(1), Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(2), Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(3), nonEmptySetId, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(4), nonEmptySetId, new Dictionary<string, decimal>(StringComparer.Ordinal)),
        };

        // Post movements -> month becomes Dirty
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var r = await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            r.Should().Be(OperationalRegisterWriteResult.Executed);
        }

        // Finalize -> runner calls projector -> projector writes turnovers+balances
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            var finalized = await runner.FinalizeRegisterDirtyAsync(
                registerId,
                maxPeriods: 10,
                manageTransaction: true,
                ct: CancellationToken.None);

            finalized.Should().Be(1);
        }

        // Verify projections
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            var t = await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 1, 15), ct: CancellationToken.None);
            var b = await balances.GetByMonthAsync(registerId, new DateOnly(2026, 1, 1), ct: CancellationToken.None);

            t.Should().HaveCount(2);
            b.Should().HaveCount(2);

            GetMovementCount(t, Guid.Empty).Should().Be(3);
            GetMovementCount(t, nonEmptySetId).Should().Be(2);

            GetMovementCount(b, Guid.Empty).Should().Be(3);
            GetMovementCount(b, nonEmptySetId).Should().Be(2);
        }

        // Finalization row must be Finalized
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var fin = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();
            var row = await fin.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);

            row.Should().NotBeNull();
            row!.Status.Should().Be(OperationalRegisterFinalizationStatus.Finalized);
        }
    }

    private static long GetMovementCount(
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> rows,
        Guid dimensionSetId)
    {
        var row = rows.Single(r => r.DimensionSetId == dimensionSetId);
        row.Values.TryGetValue("movement_count", out var v).Should().BeTrue();
        return (long)v;
    }

    private static async Task SeedRegisterAsync(
        IHost host,
        Guid registerId,
        string code,
        string name,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources = null)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        // Seed register/resources in a committed transaction.
        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, CancellationToken.None);
        if (resources is not null)
            await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task SeedDocumentAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await docs.CreateAsync(
            new DocumentRecord
            {
                Id = documentId,
                TypeCode = "it_doc",
                Number = "IT-1",
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            },
            CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreateNonEmptyDimensionSetIdAndAllowInRegisterAsync(
        IHost host,
        Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        var rules = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var dimensionId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        // DimensionSetService enforces FK(platform_dimension_set_items.dimension_id -> platform_dimensions.dimension_id).
        // Tests must seed a dimension row explicitly.
        var code = "it_dim_" + dimensionId.ToString("N")[..8];
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO platform_dimensions (dimension_id, code, name)
            VALUES (@Id, @Code, @Name);
            """,
            new { Id = dimensionId, Code = code, Name = "Integration Test Dimension" },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        // Allow this dimension for the register, otherwise movements validation must reject non-empty DimensionSetId.
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        await rules.ReplaceAsync(
            registerId,
            new[]
            {
                new OperationalRegisterDimensionRule(
                    dimensionId,
                    code,
                    Ordinal: 1,
                    IsRequired: false)
            },
            nowUtc,
            CancellationToken.None);

        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dimensionId, valueId)
        });

        await uow.CommitAsync(CancellationToken.None);

        await uow.BeginTransactionAsync(CancellationToken.None);
        var id = await svc.GetOrCreateIdAsync(bag, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        return id;
    }
}
