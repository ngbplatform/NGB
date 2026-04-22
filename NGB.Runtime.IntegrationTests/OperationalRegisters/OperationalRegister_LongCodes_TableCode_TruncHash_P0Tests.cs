using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: PostgreSQL identifiers are capped at 63 chars. Long register codes must not silently collapse
/// into the same physical per-register table names after truncation.
///
/// We guarantee this by using a deterministic hash suffix when table_code normalization needs truncation,
/// and by enforcing uniqueness of table_code in the DB.
///
/// This test verifies:
/// - NormalizeTableCode(long) ends with _[0-9a-f]{12} and length <= 46 (so opreg_*__movements fits in 63)
/// - two long codes with the same prefix produce different table_code values
/// - applying movements creates two distinct per-register movements tables and persists rows independently
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegister_LongCodes_TableCode_TruncHash_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task LongRegisterCodes_UseHashedTableCodes_AndPerRegisterTablesDoNotCollide()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Long codes with a shared prefix that would collide under naive truncation.
        var sharedPrefix = new string('r', 90);
        var code1 = sharedPrefix + "_tail_1";
        var code2 = sharedPrefix + "_tail_2";

        var codeNorm1 = OperationalRegisterId.NormalizeCode(code1);
        var codeNorm2 = OperationalRegisterId.NormalizeCode(code2);

        var expectedTableCode1 = OperationalRegisterNaming.NormalizeTableCode(codeNorm1);
        var expectedTableCode2 = OperationalRegisterNaming.NormalizeTableCode(codeNorm2);

        expectedTableCode1.Should().NotBe(expectedTableCode2, "hash suffix must prevent truncation collisions for long codes");

        expectedTableCode1.Length.Should().BeLessThanOrEqualTo(46);
        expectedTableCode2.Length.Should().BeLessThanOrEqualTo(46);
        expectedTableCode1.Should().MatchRegex("^[a-z0-9_]+$");
        expectedTableCode2.Should().MatchRegex("^[a-z0-9_]+$");
        expectedTableCode1.Should().MatchRegex("_[0-9a-f]{12}$");
        expectedTableCode2.Should().MatchRegex("_[0-9a-f]{12}$");

        Guid registerId1;
        Guid registerId2;

        // Create two different registers with long codes and a simple resource set.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            registerId1 = await svc.UpsertAsync(code1, "Long Code Register 1", CancellationToken.None);
            registerId2 = await svc.UpsertAsync(code2, "Long Code Register 2", CancellationToken.None);

            await svc.ReplaceResourcesAsync(registerId1,
                [new OperationalRegisterResourceDefinition("Amount", "Amount", 1)],
                CancellationToken.None);

            await svc.ReplaceResourcesAsync(registerId2,
                [new OperationalRegisterResourceDefinition("Amount", "Amount", 1)],
                CancellationToken.None);
        }

        // Seed two documents (write_log has an FK to documents).
        var documentId1 = Guid.CreateVersion7();
        var documentId2 = Guid.CreateVersion7();
        await SeedDocumentsAsync(host, documentId1, documentId2);

        var movement1 = new OperationalRegisterMovement(
            documentId1,
            new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            Guid.Empty,
            new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 11m });

        var movement2 = new OperationalRegisterMovement(
            documentId2,
            new DateTime(2026, 1, 16, 10, 0, 0, DateTimeKind.Utc),
            Guid.Empty,
            new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 22m });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            await applier.ApplyMovementsForDocumentAsync(
                registerId1,
                documentId1,
                OperationalRegisterWriteOperation.Post,
                [movement1],
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            await applier.ApplyMovementsForDocumentAsync(
                registerId2,
                documentId2,
                OperationalRegisterWriteOperation.Post,
                [movement2],
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        // Verify: DB-generated table_code matches the runtime normalization, tables exist and don't collide.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var reg1 = await repo.GetByIdAsync(registerId1, CancellationToken.None);
            var reg2 = await repo.GetByIdAsync(registerId2, CancellationToken.None);

            reg1.Should().NotBeNull();
            reg2.Should().NotBeNull();

            reg1!.TableCode.Should().Be(expectedTableCode1);
            reg2!.TableCode.Should().Be(expectedTableCode2);

            var table1 = OperationalRegisterNaming.MovementsTable(reg1.TableCode);
            var table2 = OperationalRegisterNaming.MovementsTable(reg2.TableCode);

            table1.Should().NotBe(table2);
            table1.Length.Should().BeLessThanOrEqualTo(63);
            table2.Length.Should().BeLessThanOrEqualTo(63);

            var existing = (await uow.Connection.QueryAsync<string>(new CommandDefinition(
                """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name = ANY(@Names);
                """,
                new { Names = new[] { table1, table2 } },
                cancellationToken: CancellationToken.None))).AsList();

            existing.Should().Contain(table1);
            existing.Should().Contain(table2);

            var c1 = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                $"SELECT COUNT(*) FROM {table1} WHERE document_id = @D;",
                new { D = documentId1 },
                cancellationToken: CancellationToken.None));

            var c2 = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                $"SELECT COUNT(*) FROM {table2} WHERE document_id = @D;",
                new { D = documentId2 },
                cancellationToken: CancellationToken.None));

            c1.Should().Be(1);
            c2.Should().Be(1);
        }
    }

    private static async Task SeedDocumentsAsync(IHost host, params Guid[] ids)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        foreach (var id in ids)
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = id,
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
