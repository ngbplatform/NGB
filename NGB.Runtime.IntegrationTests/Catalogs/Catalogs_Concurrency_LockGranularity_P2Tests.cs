using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;
using NGB.Definitions;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class Catalogs_Concurrency_LockGranularity_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // Unique catalogCode/table names to avoid colliding with real module typed tables.
    private const string CatalogCode = "it_cat_conc";
    private const string TypedTable = "cat_it_cat_conc";

    [Fact]
    public async Task CreateAsync_TwoConcurrentCreates_SameCatalogCode_BothSucceed_AndCreateTwoTypedRows()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        var counter = new CallCounter();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddSingleton(counter);
                services.AddScoped<CountingCatalogTypeStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<CountingCatalogTypeStorage>());
            });

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Guid id1 = Guid.Empty;
        Guid id2 = Guid.Empty;

        var t1 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            id1 = await drafts.CreateAsync(CatalogCode, manageTransaction: true, ct: CancellationToken.None);
        });

        var t2 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            id2 = await drafts.CreateAsync(CatalogCode, manageTransaction: true, ct: CancellationToken.None);
        });

        start.SetResult();
        await Task.WhenAll(t1, t2);

        id1.Should().NotBeEmpty();
        id2.Should().NotBeEmpty();
        id1.Should().NotBe(id2);

        counter.EnsureCalls.Should().Be(2, "each create must create its typed row independently");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var catCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM catalogs WHERE catalog_code = @c;",
            new { c = CatalogCode });

        catCount.Should().Be(2);

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE catalog_id = ANY(@ids);",
            new { ids = new[] { id1, id2 } });

        typedCount.Should().Be(2);
    }

    [Fact]
    public async Task MarkForDeletionAsync_TwoConcurrentMarks_SameCatalog_BothSucceed_AndTypedRowRemains()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        var counter = new CallCounter();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddSingleton(counter);
                services.AddScoped<CountingCatalogTypeStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<CountingCatalogTypeStorage>());
            });

        Guid catalogId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync(CatalogCode, manageTransaction: true, ct: CancellationToken.None);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var d1 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None);
        });

        var d2 = Task.Run(async () =>
        {
            await start.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None);
        });

        start.SetResult();
        await Task.WhenAll(d1, d2);

        counter.DeleteCalls.Should().Be(0,
            "CatalogDraftService uses strict soft delete, so it must not call typed storage DeleteAsync.");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var isDeleted = await conn.ExecuteScalarAsync<bool>(
            "SELECT is_deleted FROM catalogs WHERE id = @id;",
            new { id = catalogId });

        isDeleted.Should().BeTrue();

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE catalog_id = @id;",
            new { id = catalogId });

        typedCount.Should().Be(1);
    }

    private static async Task EnsureTypedTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    catalog_id UUID PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    name TEXT NOT NULL DEFAULT '',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
""";

        await conn.ExecuteAsync(ddl);
    }

    sealed class CallCounter
    {
        private int _ensureCalls;
        private int _deleteCalls;

        public int EnsureCalls => Volatile.Read(ref _ensureCalls);
        public int DeleteCalls => Volatile.Read(ref _deleteCalls);

        public void IncEnsure() => Interlocked.Increment(ref _ensureCalls);
        public void IncDelete() => Interlocked.Increment(ref _deleteCalls);
    }

    sealed class CountingCatalogTypeStorage(IUnitOfWork uow, CallCounter counter) : ICatalogTypeStorage
    {
        public string CatalogCode => Catalogs_Concurrency_LockGranularity_P2Tests.CatalogCode;

        public async Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default)
        {
            counter.IncEnsure();
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (catalog_id, name) VALUES (@catalogId, 'test') ON CONFLICT (catalog_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { catalogId }, uow.Transaction, cancellationToken: ct));
        }

        public async Task DeleteAsync(Guid catalogId, CancellationToken ct = default)
        {
            counter.IncDelete();
            uow.EnsureActiveTransaction();

            var sql = $"DELETE FROM {TypedTable} WHERE catalog_id = @catalogId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { catalogId }, uow.Transaction, cancellationToken: ct));
        }
    }
}
