using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Catalogs.Exceptions;
using NGB.Definitions;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogDraftService_TypedStorage_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // Use unique catalogCode/table names to avoid colliding with real module typed tables.
    private const string CatalogCode = "it_cat_ts";
    private const string TypedTable = "cat_it_cat_ts";

    [Fact]
    public async Task CreateAsync_WithTypedStorage_CreatesRegistryRowAndTypedRow()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ItCatalogTypeStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<ItCatalogTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        var id = await drafts.CreateAsync(CatalogCode);

        var cat = await repo.GetAsync(id);
        cat.Should().NotBeNull();
        cat!.CatalogCode.Should().Be(CatalogCode);
        cat.IsDeleted.Should().BeFalse();

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE catalog_id = @id;",
            new { id });

        typedCount.Should().Be(1);
    }

    [Fact]
    public async Task MarkForDeletionAsync_DoesNotDeleteTypedRow_AndIsIdempotent_NoStorageDeleteCall()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ItCatalogTypeStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<ItCatalogTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<ItCatalogTypeStorage>();

        var id = await drafts.CreateAsync(CatalogCode);

        // First delete
        await drafts.MarkForDeletionAsync(id);

        var cat = await repo.GetAsync(id);
        cat.Should().NotBeNull();
        cat!.IsDeleted.Should().BeTrue();

        // Second mark -> idempotent and should NOT call storage.DeleteAsync (soft delete only).
        await drafts.MarkForDeletionAsync(id);

        storage.DeleteCalls.Should().Be(0);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE catalog_id = @id;",
            new { id });

        typedCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenTypedStorageThrows_RollsBackRegistryAndTyped()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<FailAfterInsertCatalogTypeStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<FailAfterInsertCatalogTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();

        var act = () => drafts.CreateAsync(CatalogCode);

        var ex = await act.Should().ThrowAsync<CatalogTypedStorageOperationException>();
        ex.Which.AssertNgbError(
            "catalog.typed_storage.operation_failed",
            "catalogId",
            "catalogCode",
            "operation",
            "details");
        ex.Which.InnerException.Should().BeOfType<NotSupportedException>();
        ex.Which.InnerException!.Message.Should().Contain("simulated catalog storage failure");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var catCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM catalogs WHERE catalog_code = @c;",
            new { c = CatalogCode });

        catCount.Should().Be(0);

        var typedCount = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable};");
        typedCount.Should().Be(0);
    }

    [Fact]
    public async Task MarkForDeletionAsync_WhenTypedStorageDeleteWouldThrow_DoesNotCallDelete_AndMarksDeleted_AndKeepsTypedRow()
    {
        await EnsureTypedTableExistsAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                // This storage throws from DeleteAsync; soft delete must not call it.
                services.AddScoped<FailAfterDeleteCatalogTypeStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<FailAfterDeleteCatalogTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        var id = await drafts.CreateAsync(CatalogCode);

        // Act
        await drafts.MarkForDeletionAsync(id);

        // Assert
        var cat = await repo.GetAsync(id);
        cat.Should().NotBeNull();
        cat!.IsDeleted.Should().BeTrue();

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var typedCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {TypedTable} WHERE catalog_id = @id;",
            new { id });

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

    sealed class ItCatalogTypeStorage(IUnitOfWork uow) : ICatalogTypeStorage
    {
        public int EnsureCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public string CatalogCode => CatalogDraftService_TypedStorage_P0Tests.CatalogCode;

        public async Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default)
        {
            EnsureCalls++;
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (catalog_id, name) VALUES (@catalogId, 'test') ON CONFLICT (catalog_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { catalogId }, uow.Transaction, cancellationToken: ct));
        }

        public async Task DeleteAsync(Guid catalogId, CancellationToken ct = default)
        {
            DeleteCalls++;
            uow.EnsureActiveTransaction();

            var sql = $"DELETE FROM {TypedTable} WHERE catalog_id = @catalogId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { catalogId }, uow.Transaction, cancellationToken: ct));
        }
    }

    sealed class FailAfterInsertCatalogTypeStorage(IUnitOfWork uow) : ICatalogTypeStorage
    {
        public string CatalogCode => CatalogDraftService_TypedStorage_P0Tests.CatalogCode;

        public async Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (catalog_id, name) VALUES (@catalogId, 'test') ON CONFLICT (catalog_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { catalogId }, uow.Transaction, cancellationToken: ct));

            throw new NotSupportedException("simulated catalog storage failure");
        }

        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    sealed class FailAfterDeleteCatalogTypeStorage(IUnitOfWork uow) : ICatalogTypeStorage
    {
        public string CatalogCode => CatalogDraftService_TypedStorage_P0Tests.CatalogCode;

        public async Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (catalog_id, name) VALUES (@catalogId, 'test') ON CONFLICT (catalog_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { catalogId }, uow.Transaction, cancellationToken: ct));
        }

        public async Task DeleteAsync(Guid catalogId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"DELETE FROM {TypedTable} WHERE catalog_id = @catalogId;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { catalogId }, uow.Transaction, cancellationToken: ct));

            throw new NotSupportedException("simulated catalog delete failure");
        }
    }
}
