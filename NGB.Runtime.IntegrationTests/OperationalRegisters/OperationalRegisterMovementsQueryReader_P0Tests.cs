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
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Read-side query boundary for per-register movements tables (opreg_*__movements).
///
/// Ensures:
/// - query reader returns rows ordered by movement_id
/// - paging by afterMovementId is stable (no duplicates / no gaps)
/// - filters: dimension_set_id, dimension values (AND), document_id, is_storno
/// - enrichment: DimensionBag and DimensionValueDisplays for non-empty sets
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovementsQueryReader_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetByMonthsAsync_PagingByMovementId_IsStable_AndDoesNotDuplicateRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();
        var code = "it_mov_q_" + registerId.ToString("N")[..8];

        await SeedRegisterAndDocumentsAsync(host, registerId, code, new[] { doc1, doc2 }, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 10)
        });

        var jan = new DateOnly(2026, 1, 1);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var d1 = new[]
            {
                new OperationalRegisterMovement(doc1, new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc), Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 1m }),
                new OperationalRegisterMovement(doc1, new DateTime(2026, 1, 11, 10, 0, 0, DateTimeKind.Utc), Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 2m }),
                new OperationalRegisterMovement(doc1, new DateTime(2026, 1, 12, 10, 0, 0, DateTimeKind.Utc), Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 3m })
            };

            var d2 = new[]
            {
                new OperationalRegisterMovement(doc2, new DateTime(2026, 1, 13, 10, 0, 0, DateTimeKind.Utc), Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 4m }),
                new OperationalRegisterMovement(doc2, new DateTime(2026, 1, 14, 10, 0, 0, DateTimeKind.Utc), Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 5m })
            };

            (await applier.ApplyMovementsForDocumentAsync(
                registerId,
                doc1,
                OperationalRegisterWriteOperation.Post,
                d1,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);

            (await applier.ApplyMovementsForDocumentAsync(
                registerId,
                doc2,
                OperationalRegisterWriteOperation.Post,
                d2,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsQueryReader>();

            var page1 = await reader.GetByMonthsAsync(registerId, jan, jan, afterMovementId: null, limit: 2, ct: CancellationToken.None);
            var page2 = await reader.GetByMonthsAsync(registerId, jan, jan, afterMovementId: page1[^1].MovementId, limit: 2, ct: CancellationToken.None);
            var page3 = await reader.GetByMonthsAsync(registerId, jan, jan, afterMovementId: page2[^1].MovementId, limit: 2, ct: CancellationToken.None);

            page1.Should().HaveCount(2);
            page2.Should().HaveCount(2);
            page3.Should().HaveCount(1);

            var all = page1.Concat(page2).Concat(page3).ToArray();

            all.Should().HaveCount(5);
            all.Select(x => x.MovementId).Should().OnlyHaveUniqueItems();
            all.Select(x => x.MovementId).Should().BeInAscendingOrder();

            all.All(x => x.PeriodMonth == jan).Should().BeTrue();
            all.All(x => x.IsStorno == false).Should().BeTrue();
            all.All(x => x.DimensionSetId == Guid.Empty).Should().BeTrue();
            all.All(x => x.Dimensions.IsEmpty).Should().BeTrue();

            all.Select(x => x.DocumentId).Distinct().Should().BeEquivalentTo(new[] { doc1, doc2 });

            all.Select(x => x.Values["amount"]).Should().BeEquivalentTo(new[] { 1m, 2m, 3m, 4m, 5m });
        }
    }

    [Fact]
    public async Task GetByMonthsAsync_Filters_DimensionSetId_DimensionsAnd_Document_And_Storno_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.NewGuid();
        var doc1 = Guid.NewGuid();
        var doc2 = Guid.NewGuid();
        var code = "it_mov_f_" + registerId.ToString("N")[..8];

        await SeedRegisterAndDocumentsAsync(host, registerId, code, new[] { doc1, doc2 }, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 10)
        });

        // Prepare two dimensions and three dimension sets (A, B, AB), and allow both dimensions for the register.
        var (dim1, val1, dim2, val2, setA, setB, setAB) = await CreateTwoDimensionSetsAndAllowInRegisterAsync(host, registerId);

        var jan = new DateOnly(2026, 1, 1);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            // doc1: setAB
            var d1 = new[]
            {
                new OperationalRegisterMovement(doc1, new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc), setAB,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 10m }),
                new OperationalRegisterMovement(doc1, new DateTime(2026, 1, 11, 10, 0, 0, DateTimeKind.Utc), setAB,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 20m })
            };

            // doc2: setA only
            var d2 = new[]
            {
                new OperationalRegisterMovement(doc2, new DateTime(2026, 1, 12, 10, 0, 0, DateTimeKind.Utc), setA,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 30m })
            };

            (await applier.ApplyMovementsForDocumentAsync(registerId, doc1, OperationalRegisterWriteOperation.Post, d1, null, true, CancellationToken.None))
                .Should().Be(OperationalRegisterWriteResult.Executed);

            (await applier.ApplyMovementsForDocumentAsync(registerId, doc2, OperationalRegisterWriteOperation.Post, d2, null, true, CancellationToken.None))
                .Should().Be(OperationalRegisterWriteResult.Executed);

            // Unpost doc1 => append storno for its movements.
            (await applier.ApplyMovementsForDocumentAsync(registerId, doc1, OperationalRegisterWriteOperation.Unpost,
                movements: Array.Empty<OperationalRegisterMovement>(),
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsQueryReader>();

            // 1) Filter by DimensionSetId (AB) => 2 original + 2 storno.
            var allAb = await reader.GetByMonthsAsync(registerId, jan, jan,
                dimensionSetId: setAB,
                limit: 1000,
                ct: CancellationToken.None);

            allAb.Should().HaveCount(4);
            allAb.All(x => x.DimensionSetId == setAB).Should().BeTrue();

            // 2) isStorno=false over AB => only originals.
            var abOriginals = await reader.GetByMonthsAsync(registerId, jan, jan,
                dimensionSetId: setAB,
                isStorno: false,
                limit: 1000,
                ct: CancellationToken.None);

            abOriginals.Should().HaveCount(2);
            abOriginals.All(x => x.IsStorno == false).Should().BeTrue();
            abOriginals.Select(x => x.Values["amount"]).Should().BeEquivalentTo(new[] { 10m, 20m });

            // 3) AND filter by both dimension values => matches only AB rows.
            var byBoth = await reader.GetByMonthsAsync(registerId, jan, jan,
                dimensions: new[]
                {
                    new DimensionValue(dim1, val1),
                    new DimensionValue(dim2, val2)
                },
                isStorno: false,
                limit: 1000,
                ct: CancellationToken.None);

            byBoth.Should().HaveCount(2);
            byBoth.All(x => x.DimensionSetId == setAB).Should().BeTrue();

            // 4) Filter by just dim1 => matches setA and setAB.
            var byDim1 = await reader.GetByMonthsAsync(registerId, jan, jan,
                dimensions: new[] { new DimensionValue(dim1, val1) },
                isStorno: false,
                limit: 1000,
                ct: CancellationToken.None);

            byDim1.Should().HaveCount(3);
            byDim1.Select(x => x.Values["amount"]).Should().BeEquivalentTo(new[] { 10m, 20m, 30m });

            // 5) Document filter + storno => two storno rows for doc1.
            var doc1Storno = await reader.GetByMonthsAsync(registerId, jan, jan,
                documentId: doc1,
                isStorno: true,
                limit: 1000,
                ct: CancellationToken.None);

            doc1Storno.Should().HaveCount(2);
            doc1Storno.All(x => x.DocumentId == doc1).Should().BeTrue();
            doc1Storno.All(x => x.IsStorno).Should().BeTrue();

            // 6) DimensionSetId=setA combined with dim2 filter => empty.
            var impossible = await reader.GetByMonthsAsync(registerId, jan, jan,
                dimensions: new[] { new DimensionValue(dim2, val2) },
                dimensionSetId: setA,
                limit: 1000,
                ct: CancellationToken.None);

            impossible.Should().BeEmpty();

            // Enrichment sanity: for non-empty sets, Dimensions and displays are populated.
            var one = byBoth[0];
            one.Dimensions.Items.Should().HaveCount(2);
            // DimensionValueDisplays is keyed by DimensionId (not ValueId).
            one.DimensionValueDisplays.Should().ContainKey(dim1);
            one.DimensionValueDisplays[dim1].Should().NotBeNullOrWhiteSpace();
            one.DimensionValueDisplays.Should().ContainKey(dim2);
            one.DimensionValueDisplays[dim2].Should().NotBeNullOrWhiteSpace();

            // The extra setB is not used by movements, but should still have been created deterministically.
            setB.Should().NotBe(Guid.Empty);
        }
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
