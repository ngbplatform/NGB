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
/// P0: UI/report oriented facade for Operational Registers read-side.
///
/// Covers:
/// - Movements paging envelope (keyset by MovementId) + filters + storno.
/// - Projections (turnovers/balances) paging envelope with cursor (PeriodMonth, DimensionSetId)
///   + filters (DimensionSetId, DimensionValue AND).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterReadService_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetMovementsPageAsync_PagingAndFilters_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.NewGuid();
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();

        // Per-register tables are created dynamically and may not be fully dropped by Respawn.
        // Always use a unique code per test.
        var code = "it_rs_mov_" + registerId.ToString("N")[..8];

        await SeedRegisterAndDocumentsAsync(host, registerId, code, new[] { doc1, doc2 }, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 10)
        });

        var (dim1, val1, dim2, val2, setA, _setB, setAB) = await CreateTwoDimensionSetsAndAllowInRegisterAsync(host, registerId);

        var jan = new DateOnly(2026, 1, 1);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            // doc1: setAB (2 rows)
            var d1 = new[]
            {
                new OperationalRegisterMovement(doc1, new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc), setAB,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 10m }),
                new OperationalRegisterMovement(doc1, new DateTime(2026, 1, 11, 10, 0, 0, DateTimeKind.Utc), setAB,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 20m })
            };

            // doc2: setA only (1 row)
            var d2 = new[]
            {
                new OperationalRegisterMovement(doc2, new DateTime(2026, 1, 12, 10, 0, 0, DateTimeKind.Utc), setA,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 30m })
            };

            (await applier.ApplyMovementsForDocumentAsync(registerId, doc1, OperationalRegisterWriteOperation.Post, d1, null, true, CancellationToken.None))
                .Should().Be(OperationalRegisterWriteResult.Executed);

            (await applier.ApplyMovementsForDocumentAsync(registerId, doc2, OperationalRegisterWriteOperation.Post, d2, null, true, CancellationToken.None))
                .Should().Be(OperationalRegisterWriteResult.Executed);

            // Unpost doc1 => storno appended.
            (await applier.ApplyMovementsForDocumentAsync(registerId, doc1, OperationalRegisterWriteOperation.Unpost,
                movements: Array.Empty<OperationalRegisterMovement>(),
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            // AND filter by both dimension values (with duplicates / out-of-order) + isStorno=false
            // must match only setAB originals.
            var req1 = new OperationalRegisterMovementsPageRequest(
                RegisterId: registerId,
                FromInclusive: jan,
                ToInclusive: jan,
                Dimensions: new[]
                {
                    new DimensionValue(dim2, val2),
                    new DimensionValue(dim1, val1),
                    new DimensionValue(dim2, val2) // duplicate to verify canonicalization
                },
                DimensionSetId: null,
                DocumentId: null,
                IsStorno: false,
                Cursor: null,
                PageSize: 1);

            var page1 = await svc.GetMovementsPageAsync(req1, CancellationToken.None);
            page1.Lines.Should().HaveCount(1);
            page1.HasMore.Should().BeTrue();
            page1.NextCursor.Should().NotBeNull();
            page1.Lines[0].DimensionSetId.Should().Be(setAB);
            page1.Lines[0].IsStorno.Should().BeFalse();

            var page2 = await svc.GetMovementsPageAsync(req1 with { Cursor = page1.NextCursor }, CancellationToken.None);
            page2.Lines.Should().HaveCount(1);
            page2.HasMore.Should().BeFalse();
            page2.NextCursor.Should().BeNull();

            var all = page1.Lines.Concat(page2.Lines).ToArray();
            all.Should().HaveCount(2);
            all.Select(x => x.MovementId).Should().BeInAscendingOrder();
            all.All(x => x.DimensionSetId == setAB).Should().BeTrue();
            all.Select(x => x.Values["amount"]).Should().BeEquivalentTo(new[] { 10m, 20m });

            // Storno filter: AB + doc1 + isStorno=true => exactly 2 storno rows.
            var storno = await svc.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: registerId,
                    FromInclusive: jan,
                    ToInclusive: jan,
                    Dimensions: null,
                    DimensionSetId: setAB,
                    DocumentId: doc1,
                    IsStorno: true,
                    Cursor: null,
                    PageSize: 50),
                CancellationToken.None);

            storno.HasMore.Should().BeFalse();
            storno.Lines.Should().HaveCount(2);
            storno.Lines.All(x => x.IsStorno).Should().BeTrue();
            storno.Lines.All(x => x.DocumentId == doc1).Should().BeTrue();

            // Enrichment sanity for non-empty sets.
            var one = all[0];
            one.Dimensions.IsEmpty.Should().BeFalse();
            one.DimensionValueDisplays.Should().ContainKey(dim1);
            one.DimensionValueDisplays[dim1].Should().NotBeNullOrWhiteSpace();
            one.DimensionValueDisplays.Should().ContainKey(dim2);
            one.DimensionValueDisplays[dim2].Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task GetProjectionPagesAsync_TurnoversAndBalances_PagingAndFilters_Work()
    {
        // Unique register code per test (dynamic per-register tables are not necessarily dropped by Respawn).
        var code = "it_rs_prj_" + Guid.CreateVersion7().ToString("N")[..8];
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
        await SeedRegisterAsync(host, registerId, code, name: "IT Projections", resources: new[]
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

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();
            (await applier.ApplyMovementsForDocumentAsync(registerId, documentId, OperationalRegisterWriteOperation.Post, movements, null, true, CancellationToken.None))
                .Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            // Before finalization derived tables may not exist -> service returns empty.
            var before = await svc.GetTurnoversPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(registerId, janMonth, janMonth, PageSize: 10),
                CancellationToken.None);
            before.Lines.Should().BeEmpty();
            before.HasMore.Should().BeFalse();
            before.NextCursor.Should().BeNull();
        }

        // Finalize => projector writes turnovers/balances.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

            var finalized = await runner.FinalizeRegisterDirtyAsync(registerId, maxPeriods: 10, manageTransaction: true, ct: CancellationToken.None);
            finalized.Should().Be(1);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            // Paging (PeriodMonth, DimensionSetId) with pageSize=1 => two pages.
            var page1 = await svc.GetTurnoversPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(registerId, janMonth, janMonth, PageSize: 1),
                CancellationToken.None);

            page1.Lines.Should().HaveCount(1);
            page1.HasMore.Should().BeTrue();
            page1.NextCursor.Should().NotBeNull();
            page1.Lines[0].PeriodMonth.Should().Be(janMonth);
            page1.Lines[0].DimensionSetId.Should().Be(Guid.Empty);
            page1.Lines[0].Values["movement_count"].Should().Be(3m);

            var page2 = await svc.GetTurnoversPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(registerId, janMonth, janMonth, Cursor: page1.NextCursor, PageSize: 10),
                CancellationToken.None);

            page2.Lines.Should().HaveCount(1);
            page2.HasMore.Should().BeFalse();
            page2.NextCursor.Should().BeNull();
            page2.Lines[0].DimensionSetId.Should().Be(nonEmptySetId);
            page2.Lines[0].Values["movement_count"].Should().Be(2m);

            // DimensionValue filter => only non-empty row.
            var byDim = await svc.GetTurnoversPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(
                    RegisterId: registerId,
                    FromInclusive: janMonth,
                    ToInclusive: janMonth,
                    Dimensions: new[] { new DimensionValue(dimensionId, valueId) },
                    DimensionSetId: null,
                    Cursor: null,
                    PageSize: 10),
                CancellationToken.None);

            byDim.Lines.Should().ContainSingle();
            byDim.Lines[0].DimensionSetId.Should().Be(nonEmptySetId);
            byDim.Lines[0].Values["movement_count"].Should().Be(2m);
            byDim.Lines[0].DimensionValueDisplays.Should().ContainKey(dimensionId);

            // DimensionSetId filter => only that row.
            var bySet = await svc.GetTurnoversPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(registerId, janMonth, janMonth, DimensionSetId: nonEmptySetId, PageSize: 10),
                CancellationToken.None);

            bySet.Lines.Should().ContainSingle();
            bySet.Lines[0].Values["movement_count"].Should().Be(2m);

            // Balances use the same payload in MovementsCountProjector.
            var balances = await svc.GetBalancesPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(registerId, janMonth, janMonth, PageSize: 10),
                CancellationToken.None);

            balances.Lines.Should().HaveCount(2);
            balances.Lines.Single(x => x.DimensionSetId == nonEmptySetId).Values["movement_count"].Should().Be(2m);
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

    private static async Task SeedRegisterAndDocumentsAsync(
        IHost host,
        Guid registerId,
        string registerCode,
        IReadOnlyList<Guid> documentIds,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources = null)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await regRepo.UpsertAsync(
            new OperationalRegisterUpsert(registerId, registerCode, "Integration Test Register"),
            nowUtc,
            CancellationToken.None);

        if (resources is not null)
            await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);

        foreach (var documentId in documentIds)
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = documentId,
                TypeCode = "it_doc",
                Number = null,
                DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, CancellationToken.None);
        }

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
                new OperationalRegisterDimensionRule(dimensionId, code, Ordinal: 1, IsRequired: false)
            },
            nowUtc,
            CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);

        await uow.BeginTransactionAsync(CancellationToken.None);
        var setId = await svc.GetOrCreateIdAsync(new DimensionBag(new[] { new DimensionValue(dimensionId, valueId) }), CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        setId.Should().NotBe(Guid.Empty);

        return (dimensionId, valueId, setId);
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

        // DimensionSetService enforces FK(platform_dimension_set_items.dimension_id -> platform_dimensions.dimension_id).
        // Tests must seed platform_dimensions explicitly.
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

        // Allow both dimensions for this register.
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

        // Create dimension sets in a committed transaction.
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
