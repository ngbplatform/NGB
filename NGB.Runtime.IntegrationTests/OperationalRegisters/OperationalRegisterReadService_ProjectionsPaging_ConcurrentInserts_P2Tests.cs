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
/// P2: Projection paging contract under concurrent inserts.
///
/// Turnovers/Balances paging is keyset by (PeriodMonth, DimensionSetId).
/// If a new projection row appears after a cursor has been issued, continuing from the old cursor must:
/// - not drop any previously existing rows after that cursor
/// - include the new row if its key is after the cursor
///
/// Note: projections use replace-per-month semantics (derived tables), so the new row is materialized
/// after another finalize() run.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterReadService_ProjectionsPaging_ConcurrentInserts_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TurnoversPaging_WhenNewDimensionSetRowAppearsAfterCursor_IsIncluded_AndDoesNotDropExistingRows()
    {
        // Unique register code per test (dynamic per-register tables may not be dropped by Respawn).
        var code = "it_rs_prj_pg_" + Guid.CreateVersion7().ToString("N")[..8];
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
        await SeedRegisterAsync(host, registerId, code, name: "IT Projections Paging", resources: new[]
        {
            new OperationalRegisterResourceDefinition("movement_count", "Movement Count", 1)
        });

        // Allow one dimension for this register and create two non-empty sets for that dimension.
        var (dimensionId, setA, setB) = await CreateTwoSetsForOneDimensionAndAllowInRegisterAsync(host, registerId);

        var docA = Guid.CreateVersion7();
        var docB = Guid.CreateVersion7();
        await SeedDocumentsAsync(host, docA, docB);

        var janMonth = new DateOnly(2026, 1, 1);
        var jan10 = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        // Initial: 3 empty + 2 setA => projections materialize rows for {empty, setA}.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var initial = new[]
            {
                new OperationalRegisterMovement(docA, jan10.AddDays(0), Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal)),
                new OperationalRegisterMovement(docA, jan10.AddDays(1), Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal)),
                new OperationalRegisterMovement(docA, jan10.AddDays(2), Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal)),
                new OperationalRegisterMovement(docA, jan10.AddDays(3), setA, new Dictionary<string, decimal>(StringComparer.Ordinal)),
                new OperationalRegisterMovement(docA, jan10.AddDays(4), setA, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            };

            (await applier.ApplyMovementsForDocumentAsync(registerId, docA, OperationalRegisterWriteOperation.Post, initial, null, true, CancellationToken.None))
                .Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await FinalizeDirtyAsync(host, registerId);

        OperationalRegisterMonthlyProjectionPageCursor cursor;

        // Page1: pageSize=1 => first row must be (janMonth, Guid.Empty)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            var page1 = await svc.GetTurnoversPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(registerId, janMonth, janMonth, PageSize: 1),
                CancellationToken.None);

            page1.Lines.Should().HaveCount(1);
            page1.HasMore.Should().BeTrue();
            page1.NextCursor.Should().NotBeNull();

            page1.Lines[0].PeriodMonth.Should().Be(janMonth);
            page1.Lines[0].DimensionSetId.Should().Be(Guid.Empty);
            page1.Lines[0].Values["movement_count"].Should().Be(3m);

            cursor = page1.NextCursor!;
        }

        // Between pages: add movements for a NEW dimension set (setB) and finalize again.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var inserted = new[]
            {
                new OperationalRegisterMovement(docB, jan10.AddDays(5), setB, new Dictionary<string, decimal>(StringComparer.Ordinal)),
            };

            (await applier.ApplyMovementsForDocumentAsync(registerId, docB, OperationalRegisterWriteOperation.Post, inserted, null, true, CancellationToken.None))
                .Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await FinalizeDirtyAsync(host, registerId);

        // Continue paging from the old cursor (after (janMonth, Guid.Empty)).
        // Must include BOTH existing setA row and the newly materialized setB row.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            var next = await svc.GetTurnoversPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(registerId, janMonth, janMonth, Cursor: cursor, PageSize: 10),
                CancellationToken.None);

            next.HasMore.Should().BeFalse();
            next.NextCursor.Should().BeNull();

            next.Lines.Should().HaveCount(2);
            next.Lines.Select(x => x.PeriodMonth).Distinct().Single().Should().Be(janMonth);

            var ids = next.Lines.Select(x => x.DimensionSetId).ToHashSet();
            ids.Should().Contain(setA);
            ids.Should().Contain(setB);

            next.Lines.Single(x => x.DimensionSetId == setA).Values["movement_count"].Should().Be(2m);
            next.Lines.Single(x => x.DimensionSetId == setB).Values["movement_count"].Should().Be(1m);

            // Enrichment sanity: both non-empty rows must carry displays for the allowed dimension.
            next.Lines.All(x => x.DimensionSetId != Guid.Empty).Should().BeTrue();
            next.Lines.All(x => x.DimensionValueDisplays.ContainsKey(dimensionId)).Should().BeTrue();
        }

        // Sanity: fresh first page sees 3 rows total (empty + 2 non-empty).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            var fresh = await svc.GetTurnoversPageAsync(
                new OperationalRegisterMonthlyProjectionPageRequest(registerId, janMonth, janMonth, PageSize: 10),
                CancellationToken.None);

            fresh.Lines.Should().HaveCount(3);
            fresh.Lines[0].DimensionSetId.Should().Be(Guid.Empty);
        }
    }

    private static async Task FinalizeDirtyAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

        var finalized = await runner.FinalizeRegisterDirtyAsync(registerId, maxPeriods: 10, manageTransaction: true, ct: CancellationToken.None);
        finalized.Should().BeGreaterThan(0);
    }

    private static async Task SeedRegisterAsync(
        IHost host,
        Guid registerId,
        string code,
        string name,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources)
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

    private static async Task SeedDocumentsAsync(IHost host, params Guid[] documentIds)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        foreach (var documentId in documentIds)
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = documentId,
                TypeCode = "it_doc",
                Number = null,
                DateUtc = nowUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, CancellationToken.None);
        }

        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task<(Guid DimensionId, Guid SetA, Guid SetB)> CreateTwoSetsForOneDimensionAndAllowInRegisterAsync(
        IHost host,
        Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dimSvc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        var rules = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        var dimensionId = Guid.CreateVersion7();
        var valueA = Guid.CreateVersion7();
        var valueB = Guid.CreateVersion7();

        var code = "it_dim_" + dimensionId.ToString("N")[..8];
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        // DimensionSetService enforces FK(platform_dimension_set_items.dimension_id -> platform_dimensions.dimension_id).
        await uow.BeginTransactionAsync(CancellationToken.None);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO platform_dimensions (dimension_id, code, name)
            VALUES (@Id, @Code, @Name);
            """,
            new { Id = dimensionId, Code = code, Name = "Integration Test Dimension" },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        await rules.ReplaceAsync(
            registerId,
            new[] { new OperationalRegisterDimensionRule(dimensionId, code, Ordinal: 1, IsRequired: false) },
            nowUtc,
            CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);

        // Create sets in a committed transaction.
        await uow.BeginTransactionAsync(CancellationToken.None);

        var setA = await dimSvc.GetOrCreateIdAsync(
            new DimensionBag(new[] { new DimensionValue(dimensionId, valueA) }),
            CancellationToken.None);

        var setB = await dimSvc.GetOrCreateIdAsync(
            new DimensionBag(new[] { new DimensionValue(dimensionId, valueB) }),
            CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);

        setA.Should().NotBe(Guid.Empty);
        setB.Should().NotBe(Guid.Empty);
        setA.Should().NotBe(setB);

        return (dimensionId, setA, setB);
    }
}
