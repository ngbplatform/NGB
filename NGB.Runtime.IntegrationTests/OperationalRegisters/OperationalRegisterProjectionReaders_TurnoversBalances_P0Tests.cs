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

/// <summary>
/// P0: Read-side projection readers for Operational Registers (turnovers/balances) work end-to-end.
///
/// Contract:
/// - Before finalization the derived tables may not exist -> readers must return empty results (not throw).
/// - After finalization, readers return rows enriched with DimensionBag + DimensionValueDisplays.
/// - Dimension filters (DimensionValue and/or DimensionSetId) are applied.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterProjectionReaders_TurnoversBalances_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Readers_ReturnEmptyBeforeFinalize_ThenReturnRowsWithFiltersAfterFinalize()
    {
        // Per-register tables (opreg_*__turnovers/balances) are created dynamically and may not be dropped by Respawn.
        // Tests MUST use a unique register code per test.
        var code = "rr_" + Guid.CreateVersion7().ToString("N")[..8];
        var codeNorm = code.Trim().ToLowerInvariant();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<IOperationalRegisterMonthProjector>(sp =>
                    new MovementsCountProjector(
                        registerCodeNorm: codeNorm,
                        turnovers: sp.GetRequiredService<IOperationalRegisterTurnoversStore>(),
                        balances: sp.GetRequiredService<IOperationalRegisterBalancesStore>()));
            });

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code, name: "Rent Roll", resources: new[]
        {
            new OperationalRegisterResourceDefinition("movement_count", "Movement Count", 1)
        });

        var (dimensionId, valueId, nonEmptySetId) = await CreateNonEmptyDimensionSetIdAndAllowInRegisterAsync(host, registerId);

        var documentId = Guid.CreateVersion7();
        await SeedDocumentAsync(host, documentId);

        var jan10 = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var janMonth = new DateOnly(2026, 1, 1);

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

        // Before finalization -> derived tables may not exist.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnoversReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversReader>();
            var balancesReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesReader>();

            (await turnoversReader.GetByMonthsAsync(registerId, janMonth, janMonth, ct: CancellationToken.None)).Should().BeEmpty();
            (await balancesReader.GetByMonthsAsync(registerId, janMonth, janMonth, ct: CancellationToken.None)).Should().BeEmpty();
        }

        // Finalize -> runner calls projector -> projector writes turnovers+balances.
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

        // Readers must now return projections + enrichment.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnoversReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversReader>();
            var balancesReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesReader>();

            var t = await turnoversReader.GetByMonthsAsync(registerId, janMonth, janMonth, ct: CancellationToken.None);
            var b = await balancesReader.GetByMonthsAsync(registerId, janMonth, janMonth, ct: CancellationToken.None);

            t.Should().HaveCount(2);
            b.Should().HaveCount(2);

            var tEmpty = t.Single(x => x.DimensionSetId == Guid.Empty);
            var tNonEmpty = t.Single(x => x.DimensionSetId == nonEmptySetId);

            tEmpty.PeriodMonth.Should().Be(janMonth);
            tNonEmpty.PeriodMonth.Should().Be(janMonth);

            tEmpty.Values.Should().ContainKey("movement_count");
            tNonEmpty.Values.Should().ContainKey("movement_count");

            tEmpty.Values["movement_count"].Should().Be(3m);
            tNonEmpty.Values["movement_count"].Should().Be(2m);

            // Balances use the same payload in MovementsCountProjector.
            var bNonEmpty = b.Single(x => x.DimensionSetId == nonEmptySetId);
            bNonEmpty.Values["movement_count"].Should().Be(2m);

            // Enrichment: DimensionBag must be present for non-empty set.
            tEmpty.Dimensions.IsEmpty.Should().BeTrue();
            tNonEmpty.Dimensions.IsEmpty.Should().BeFalse();
            tNonEmpty.Dimensions.Items.Should().ContainSingle(x => x.DimensionId == dimensionId && x.ValueId == valueId);

            // Enrichment: display map must include the dimension id (fallback to short GUID is allowed).
            tNonEmpty.DimensionValueDisplays.Should().ContainKey(dimensionId);
            tNonEmpty.DimensionValueDisplays[dimensionId].Should().NotBeNullOrWhiteSpace();

            // Filter by DimensionValue -> only the non-empty set row remains.
            var byDim = await turnoversReader.GetByMonthsAsync(
                registerId,
                janMonth,
                janMonth,
                dimensions: new[] { new DimensionValue(dimensionId, valueId) },
                dimensionSetId: null,
                ct: CancellationToken.None);

            byDim.Should().ContainSingle();
            byDim[0].DimensionSetId.Should().Be(nonEmptySetId);
            byDim[0].Values["movement_count"].Should().Be(2m);

            // Filter by DimensionSetId -> only that row remains.
            var bySet = await turnoversReader.GetByMonthsAsync(
                registerId,
                janMonth,
                janMonth,
                dimensions: null,
                dimensionSetId: nonEmptySetId,
                ct: CancellationToken.None);

            bySet.Should().ContainSingle();
            bySet[0].Values["movement_count"].Should().Be(2m);
        }
    }

    [Fact]
    public async Task Readers_FilterByDimensionSetId_And_ByMultipleDimensionValues()
    {
        // Unique register code: dynamic tables are not dropped by Respawn.
        var code = "rr_" + Guid.CreateVersion7().ToString("N")[..8];
        var codeNorm = code.Trim().ToLowerInvariant();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<IOperationalRegisterMonthProjector>(sp =>
                    new MovementsCountProjector(
                        registerCodeNorm: codeNorm,
                        turnovers: sp.GetRequiredService<IOperationalRegisterTurnoversStore>(),
                        balances: sp.GetRequiredService<IOperationalRegisterBalancesStore>()));
            });

        var registerId = Guid.NewGuid();
        await SeedRegisterAsync(host, registerId, code, name: "Rent Roll", resources: new[]
        {
            new OperationalRegisterResourceDefinition("movement_count", "Movement Count", 1)
        });

        var dims = await CreateTwoDimensionSetsAndAllowInRegisterAsync(host, registerId);

        var documentId = Guid.CreateVersion7();
        await SeedDocumentAsync(host, documentId);

        var jan10 = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var janMonth = new DateOnly(2026, 1, 1);

        // Mix movements across: empty set, setA (dim1), setB (dim2), and setAB (dim1+dim2).
        var movements = new[]
        {
            new OperationalRegisterMovement(documentId, jan10.AddDays(0), Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(1), dims.SetA, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(2), dims.SetA, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(3), dims.SetB, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(4), dims.SetAB, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(5), dims.SetAB, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(documentId, jan10.AddDays(6), dims.SetAB, new Dictionary<string, decimal>(StringComparer.Ordinal)),
        };

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

        // Finalize dirty month.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();
            (await runner.FinalizeRegisterDirtyAsync(registerId, maxPeriods: 10, manageTransaction: true, ct: CancellationToken.None))
                .Should().Be(1);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnoversReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversReader>();
            var balancesReader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesReader>();

            // DimensionSetId filter must work for BOTH turnovers and balances.
            var tBySet = await turnoversReader.GetByMonthsAsync(
                registerId,
                janMonth,
                janMonth,
                dimensions: null,
                dimensionSetId: dims.SetAB,
                ct: CancellationToken.None);

            tBySet.Should().ContainSingle();
            tBySet[0].DimensionSetId.Should().Be(dims.SetAB);
            tBySet[0].Values["movement_count"].Should().Be(3m);

            var bBySet = await balancesReader.GetByMonthsAsync(
                registerId,
                janMonth,
                janMonth,
                dimensions: null,
                dimensionSetId: dims.SetAB,
                ct: CancellationToken.None);

            bBySet.Should().ContainSingle();
            bBySet[0].DimensionSetId.Should().Be(dims.SetAB);
            bBySet[0].Values["movement_count"].Should().Be(3m);

            // Multiple DimensionValue filter is AND-semantics: set must contain ALL requested pairs.
            var byTwoDims = await turnoversReader.GetByMonthsAsync(
                registerId,
                janMonth,
                janMonth,
                dimensions: new[]
                {
                    new DimensionValue(dims.Dimension1Id, dims.Value1Id),
                    new DimensionValue(dims.Dimension2Id, dims.Value2Id)
                },
                dimensionSetId: null,
                ct: CancellationToken.None);

            byTwoDims.Should().ContainSingle();
            byTwoDims[0].DimensionSetId.Should().Be(dims.SetAB);
            byTwoDims[0].Values["movement_count"].Should().Be(3m);

            // Single-dimension filter should match both setA and setAB.
            var byDim1 = await turnoversReader.GetByMonthsAsync(
                registerId,
                janMonth,
                janMonth,
                dimensions: new[] { new DimensionValue(dims.Dimension1Id, dims.Value1Id) },
                dimensionSetId: null,
                ct: CancellationToken.None);

            byDim1.Should().HaveCount(2);
            byDim1.Select(x => x.DimensionSetId).Should().BeEquivalentTo(new[] { dims.SetA, dims.SetAB });

            // Combining DimensionSetId with a mismatching DimensionValue must yield empty.
            var mismatch = await turnoversReader.GetByMonthsAsync(
                registerId,
                janMonth,
                janMonth,
                dimensions: new[] { new DimensionValue(dims.Dimension2Id, dims.Value2Id) },
                dimensionSetId: dims.SetA,
                ct: CancellationToken.None);

            mismatch.Should().BeEmpty();
        }
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

    private static async Task<(Guid DimensionId, Guid ValueId, Guid DimensionSetId)> CreateNonEmptyDimensionSetIdAndAllowInRegisterAsync(
        IHost host,
        Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        var rules = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        var dimensionId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        // DimensionSetService enforces FK(platform_dimension_set_items.dimension_id -> platform_dimensions.dimension_id).
        // Tests must seed a dimension row explicitly.
        var code = "it_dim_" + dimensionId.ToString("N")[..8];

        await uow.BeginTransactionAsync(CancellationToken.None);

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

        await uow.CommitAsync(CancellationToken.None);

        // Create DimensionSetId in a committed transaction.
        await uow.BeginTransactionAsync(CancellationToken.None);

        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dimensionId, valueId)
        });

        var id = await svc.GetOrCreateIdAsync(bag, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        return (dimensionId, valueId, id);
    }

    private static async Task<(Guid Dimension1Id, Guid Value1Id, Guid Dimension2Id, Guid Value2Id, Guid SetA, Guid SetB, Guid SetAB)>
        CreateTwoDimensionSetsAndAllowInRegisterAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        var rules = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        var dim1 = Guid.NewGuid();
        var val1 = Guid.NewGuid();
        var dim2 = Guid.NewGuid();
        var val2 = Guid.NewGuid();

        var code1 = "it_dim_" + dim1.ToString("N")[..8];
        var code2 = "it_dim_" + dim2.ToString("N")[..8];

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO platform_dimensions (dimension_id, code, name)
            VALUES (@Id, @Code, @Name);
            """,
            new { Id = dim1, Code = code1, Name = "Integration Test Dimension 1" },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO platform_dimensions (dimension_id, code, name)
            VALUES (@Id, @Code, @Name);
            """,
            new { Id = dim2, Code = code2, Name = "Integration Test Dimension 2" },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        await rules.ReplaceAsync(
            registerId,
            new[]
            {
                new OperationalRegisterDimensionRule(dim1, code1, Ordinal: 1, IsRequired: false),
                new OperationalRegisterDimensionRule(dim2, code2, Ordinal: 2, IsRequired: false)
            },
            nowUtc,
            CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);

        await uow.BeginTransactionAsync(CancellationToken.None);

        var setA = await svc.GetOrCreateIdAsync(new DimensionBag(new[] { new DimensionValue(dim1, val1) }), CancellationToken.None);
        var setB = await svc.GetOrCreateIdAsync(new DimensionBag(new[] { new DimensionValue(dim2, val2) }), CancellationToken.None);
        var setAB = await svc.GetOrCreateIdAsync(new DimensionBag(new[]
        {
            new DimensionValue(dim1, val1),
            new DimensionValue(dim2, val2)
        }), CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);

        setA.Should().NotBe(Guid.Empty);
        setB.Should().NotBe(Guid.Empty);
        setAB.Should().NotBe(Guid.Empty);

        return (dim1, val1, dim2, val2, setA, setB, setAB);
    }
}
