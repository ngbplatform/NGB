using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Locks;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P0: Document/Catalog locks must use the two-int, namespaced advisory lock form:
/// pg_advisory_xact_lock(key1, key2) where key1 is the namespace (DOC\x01 / CAT\x01).
/// 
/// This makes cross-aggregate collisions impossible (Document vs Catalog), regardless of Guid values.
/// Additionally, Document/Catalog locks take two distinct keys per Guid to make Guid->lock collisions extremely unlikely.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdvisoryLocks_DocumentAndCatalog_UseNamespaceKey1_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task LockDocumentAndCatalog_UseNamespaceAsKey1()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();

        var expectedDocKey1 = AdvisoryLockNamespaces.Document;
        var expectedCatKey1 = AdvisoryLockNamespaces.Catalog;

        await uow.BeginTransactionAsync(CancellationToken.None);

        // Use Guid values with mixed bytes to ensure we observe two distinct advisory locks per aggregate.
        await locks.LockDocumentAsync(Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f"), CancellationToken.None);
        await locks.LockCatalogAsync(Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"), CancellationToken.None);

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var rows = (await uow.Connection.QueryAsync<(int Key1, int Key2)>(
            new CommandDefinition(
                "SELECT classid::int AS Key1, objid::int AS Key2 " +
                "FROM pg_locks WHERE locktype = 'advisory' AND pid = pg_backend_pid();",
                cancellationToken: CancellationToken.None))).ToList();

        rows.Count(r => r.Key1 == expectedDocKey1).Should().Be(2);
        rows.Count(r => r.Key1 == expectedCatKey1).Should().Be(2);

        await uow.RollbackAsync(CancellationToken.None);
    }
}
