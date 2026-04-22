using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Locks;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentWriteEngine_DeleteDraftStorage_Contracts_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // Use unique typeCode/table names to avoid colliding with real module typed tables.
    private const string TypeCode = "it_doc_wde";
    private const string TypedTable = "doc_it_doc_wde";

    [Fact]
    public async Task DeleteDraftStorageAsync_WithActiveTransaction_DeletesTypedRow_AndInvokesStorage()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ItDocumentTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocumentTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var engine = scope.ServiceProvider.GetRequiredService<DocumentWriteEngine>();
        var storage = scope.ServiceProvider.GetRequiredService<ItDocumentTypeStorage>();

        var documentId = Guid.CreateVersion7();

        await uow.BeginTransactionAsync();

        // Seed a typed row in the same transaction.
        var insertSql = $"INSERT INTO {TypedTable} (document_id) VALUES (@documentId) ON CONFLICT (document_id) DO NOTHING;";
        await uow.Connection.ExecuteAsync(new CommandDefinition(insertSql, new { documentId }, uow.Transaction));

        await engine.DeleteDraftStorageAsync(documentId, TypeCode, acquireLock: false);
        await uow.CommitAsync();

        storage.DeleteCalls.Should().Be(1);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE document_id = @documentId;",
            new { documentId });

        typedCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteDraftStorageAsync_WithAcquireLockTrue_HoldsDocumentAdvisoryLockUntilCommit()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ItDocumentTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocumentTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var engine = scope.ServiceProvider.GetRequiredService<DocumentWriteEngine>();

        var documentId = Guid.CreateVersion7();
        var key1 = AdvisoryLockNamespaces.Document;
        var (key2a, key2b) = GetNamespacedDocumentGuidPayloadKeys(documentId);

        await uow.BeginTransactionAsync();

        // Seed a typed row so the storage touches the DB (not required for the lock itself, but keeps the scenario realistic).
        var insertSql = $"INSERT INTO {TypedTable} (document_id) VALUES (@documentId) ON CONFLICT (document_id) DO NOTHING;";
        await uow.Connection.ExecuteAsync(new CommandDefinition(insertSql, new { documentId }, uow.Transaction));

        await engine.DeleteDraftStorageAsync(documentId, TypeCode, acquireLock: true);

        // While the UnitOfWork transaction is open, the advisory lock should be held.
        await using var conn2 = new NpgsqlConnection(Fixture.ConnectionString);
        await conn2.OpenAsync();
        await using var tx2 = await conn2.BeginTransactionAsync();

        var canAcquireA = await conn2.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "SELECT pg_try_advisory_xact_lock(@Key1, @Key2);",
                new { Key1 = key1, Key2 = key2a },
                transaction: tx2));

        var canAcquireB = await conn2.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "SELECT pg_try_advisory_xact_lock(@Key1, @Key2);",
                new { Key1 = key1, Key2 = key2b },
                transaction: tx2));

        canAcquireA.Should().BeFalse("DocumentWriteEngine must lock the document when acquireLock=true (payload key A)");
        canAcquireB.Should().BeFalse("DocumentWriteEngine must lock the document when acquireLock=true (payload key B)");

        await uow.CommitAsync();
    }

    [Fact]
    public async Task DeleteDraftStorageAsync_WithoutActiveTransaction_ThrowsFast()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ItDocumentTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<ItDocumentTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var engine = scope.ServiceProvider.GetRequiredService<DocumentWriteEngine>();

        var act = () => engine.DeleteDraftStorageAsync(Guid.CreateVersion7(), TypeCode, acquireLock: false);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    document_id UUID PRIMARY KEY,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
""";

        await conn.ExecuteAsync(ddl);
    }

    private sealed class ItDocumentTypeStorage(IUnitOfWork uow) : IDocumentTypeStorage
    {
        public int DeleteCalls { get; private set; }

        public string TypeCode => DocumentWriteEngine_DeleteDraftStorage_Contracts_P0Tests.TypeCode;

        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public async Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            DeleteCalls++;
            uow.EnsureActiveTransaction();

            var sql = $"DELETE FROM {TypedTable} WHERE document_id = @documentId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { documentId }, uow.Transaction, cancellationToken: ct));
        }
    }

    // Mirrors NGB.PostgreSql.Locks.PostgresAdvisoryLockManager logic.
    // We intentionally validate that acquireLock=true actually holds both document locks while the transaction is open.
    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;

    private static uint Avalanche(uint h)
    {
        // Murmur3 finalizer.
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }

    private static (int Key2A, int Key2B) GetGuidLockKeys(Guid id)
    {
        // We intentionally mix all 16 bytes; this is fast, stable, and allocation-free.
        Span<byte> bytes = stackalloc byte[16];
        if (!id.TryWriteBytes(bytes))
            throw new NotSupportedException("Failed to write Guid bytes.");

        uint h1 = FnvOffset;
        uint h2 = FnvOffset ^ 0x9E3779B9u; // different seed

        foreach (var b in bytes)
        {
            h1 ^= b;
            h1 *= FnvPrime;

            h2 ^= b;
            h2 *= FnvPrime;
        }

        h1 = Avalanche(h1);
        h2 = Avalanche(h2 ^ 0x85EBCA6Bu); // small post-mix tweak

        var k1 = unchecked((int)h1);
        var k2 = unchecked((int)h2);

        // Avoid obvious hotspot keys.
        if (k1 == 0) k1 = 1;
        if (k2 == 0) k2 = 2;

        // Guarantee distinct payload keys so callers can always take two locks per Guid.
        if (k2 == k1)
        {
            k2 = unchecked((int)Avalanche(unchecked((uint)k2) ^ 0x9E3779B9u));
            if (k2 == 0) k2 = 2;
            if (k2 == k1) k2 ^= unchecked((int)0xA5A5A5A5);
            if (k2 == 0) k2 = 2;
            if (k2 == k1) k2 = unchecked(k1 + 1);
        }

        return (k1, k2);
    }

    private static (int Key2A, int Key2B) GetNamespacedDocumentGuidPayloadKeys(Guid id)
    {
        var (a, b) = GetGuidLockKeys(id);
        return a <= b ? (a, b) : (b, a);
    }
}
