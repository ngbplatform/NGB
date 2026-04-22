using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Locks;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P2: Document/Catalog advisory locks must use a fixed, readable namespace in key1
/// (DOC\x01 / CAT\x01) and derive the payload keys (key2) from Guid bytes.
///
/// This pins down that key1 is stable and prevents collisions with other lock families
/// (e.g., Period locks using PER\x01).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvisoryLocks_DocumentAndCatalog_UseFixedNamespaceKey1_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task LockDocumentAndCatalog_UseFixedNamespaceKey1_AndAcquireTwoLocksPerId()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        var documentId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var catalogId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        await uow.BeginTransactionAsync(CancellationToken.None);
        await locks.LockDocumentAsync(documentId, CancellationToken.None);
        await locks.LockCatalogAsync(catalogId, CancellationToken.None);

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var rows = (await uow.Connection.QueryAsync<LockRow>(
            new CommandDefinition(
                """
                select classid::int as ClassId, objid::int as ObjId
                from pg_locks
                where locktype = 'advisory'
                  and pid = pg_backend_pid();
                """,
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None))).ToList();

        rows.Where(r => r.ClassId == AdvisoryLockNamespaces.Document)
            .Should().HaveCount(2, "document locks should acquire two payload keys under the DOC namespace");


        rows.Where(r => r.ClassId == AdvisoryLockNamespaces.Document)
            .Select(r => r.ObjId)
            .Distinct()
            .Should().HaveCount(2, "document locks must use two distinct payload keys");

                rows.Where(r => r.ClassId == AdvisoryLockNamespaces.Catalog)
            .Should().HaveCount(2, "catalog locks should acquire two payload keys under the CAT namespace");

        rows.Where(r => r.ClassId == AdvisoryLockNamespaces.Catalog)
            .Select(r => r.ObjId)
            .Distinct()
            .Should().HaveCount(2, "catalog locks must use two distinct payload keys");

        
        await uow.RollbackAsync(CancellationToken.None);
    }

    private sealed record LockRow(int ClassId, int ObjId);
}
