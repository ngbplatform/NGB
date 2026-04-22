using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P2: keyset paging stability contract for movements.
///
/// Paging for movements is by monotonically increasing MovementId. If new rows are inserted after
/// the consumer already obtained a cursor, the next page MUST return a monotonic continuation:
/// it must not skip older rows and it must include newly inserted rows (since their MovementId is higher).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterReadService_MovementsPaging_ConcurrentInserts_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetMovementsPageAsync_KeysetPaging_IsMonotonic_AndIncludesRowsInsertedAfterCursor()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        // Per-register tables are created dynamically and may not be fully dropped by Respawn.
        // Always use a unique code per test.
        var code = "it_rs_movpg_" + registerId.ToString("N")[..8];

        await SeedRegisterAndDocumentsAsync(host, registerId, code, new[] { doc1, doc2 }, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 10)
        });

        var jan = new DateOnly(2026, 1, 1);

        // Seed initial movements for doc1: 5 rows.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var baseUtc = new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc);

            var movements = Enumerable.Range(1, 5)
                .Select(i => new OperationalRegisterMovement(
                    DocumentId: doc1,
                    OccurredAtUtc: baseUtc.AddDays(i - 1),
                    DimensionSetId: Guid.Empty,
                    Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["amount"] = i * 10m
                    }))
                .ToArray();

            (await applier.ApplyMovementsForDocumentAsync(
                    registerId,
                    doc1,
                    OperationalRegisterWriteOperation.Post,
                    movements,
                    affectedPeriods: null,
                    manageTransaction: true,
                    ct: CancellationToken.None))
                .Should().Be(OperationalRegisterWriteResult.Executed);
        }

        OperationalRegisterMovementsPageCursor cursor;
        OperationalRegisterMovementQueryReadRow[] first;

        // Page 1 (2 rows)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            var page1 = await svc.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: registerId,
                    FromInclusive: jan,
                    ToInclusive: jan,
                    Dimensions: null,
                    DimensionSetId: null,
                    DocumentId: null,
                    IsStorno: null,
                    Cursor: null,
                    PageSize: 2),
                CancellationToken.None);

            page1.Lines.Should().HaveCount(2);
            page1.HasMore.Should().BeTrue();
            page1.NextCursor.Should().NotBeNull();

            first = page1.Lines.ToArray();
            cursor = page1.NextCursor!;

            first.Select(x => x.Values["amount"]).Should().Equal(new[] { 10m, 20m });
            first.Select(x => x.DocumentId).Distinct().Single().Should().Be(doc1);
            first.Select(x => x.MovementId).Should().BeInAscendingOrder();
        }

        // Insert new movements AFTER we already captured a cursor: doc2 (3 rows).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var baseUtc = new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc);

            var movements = Enumerable.Range(6, 3)
                .Select(i => new OperationalRegisterMovement(
                    DocumentId: doc2,
                    OccurredAtUtc: baseUtc.AddDays(i - 6),
                    DimensionSetId: Guid.Empty,
                    Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["amount"] = i * 10m
                    }))
                .ToArray();

            (await applier.ApplyMovementsForDocumentAsync(
                    registerId,
                    doc2,
                    OperationalRegisterWriteOperation.Post,
                    movements,
                    affectedPeriods: null,
                    manageTransaction: true,
                    ct: CancellationToken.None))
                .Should().Be(OperationalRegisterWriteResult.Executed);
        }

        // Continue paging from the old cursor.
        // Since paging is by MovementId, the new rows MUST appear after the remaining older rows.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();

            var rest = await svc.GetMovementsPageAsync(
                new OperationalRegisterMovementsPageRequest(
                    RegisterId: registerId,
                    FromInclusive: jan,
                    ToInclusive: jan,
                    Dimensions: null,
                    DimensionSetId: null,
                    DocumentId: null,
                    IsStorno: null,
                    Cursor: cursor,
                    PageSize: 100),
                CancellationToken.None);

            rest.HasMore.Should().BeFalse();
            rest.NextCursor.Should().BeNull();

            var all = first.Concat(rest.Lines).ToArray();

            all.Should().HaveCount(8);
            all.Select(x => x.MovementId).Should().BeInAscendingOrder();
            all.Select(x => x.Values["amount"]).Should().Equal(new[] { 10m, 20m, 30m, 40m, 50m, 60m, 70m, 80m });

            all.Take(5).All(x => x.DocumentId == doc1).Should().BeTrue();
            all.Skip(5).All(x => x.DocumentId == doc2).Should().BeTrue();
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
}
