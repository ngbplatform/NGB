using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Catalogs.Exceptions;
using NGB.Definitions;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogDraftService_ExternalTransactionMode_TypedStorageFailure_RollsBack_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_ts_ext";
    private const string TypedTable = "cat_it_cat_ts_ext";

    [Fact]
    public async Task CreateAsync_ManageTransactionFalse_WhenTypedStorageThrows_ExternalRollback_RemovesRegistryAndTypedRows()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAndEmptyAsync(Fixture.ConnectionString);

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
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Act
        await uow.BeginTransactionAsync();
        try
        {
            var act = () => drafts.CreateAsync(CatalogCode, manageTransaction: false);

            var ex = await act.Should().ThrowAsync<CatalogTypedStorageOperationException>();
            ex.Which.AssertNgbError(
                "catalog.typed_storage.operation_failed",
                "catalogId",
                "catalogCode",
                "operation",
                "details");
            ex.Which.InnerException.Should().BeOfType<NotSupportedException>();
            ex.Which.InnerException!.Message.Should().Contain("simulated catalog storage failure (external tx)");
        }
        finally
        {
            await uow.RollbackAsync();
        }

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var catCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM catalogs WHERE catalog_code = @c;",
            new { c = CatalogCode });

        catCount.Should().Be(0, "external rollback must undo catalog registry insert");

        var typedCount = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {TypedTable};");
        typedCount.Should().Be(0, "external rollback must undo typed storage insert");
    }

    [Fact]
    public async Task MarkForDeletionAsync_ManageTransactionFalse_WhenTypedStorageDeleteWouldThrow_DoesNotCallDelete_AndExternalCommit_MarksDeleted_AndKeepsTypedRow()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        await EnsureTypedTableExistsAndEmptyAsync(Fixture.ConnectionString);

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                // This storage throws from DeleteAsync; soft delete must not call it.
                services.AddScoped<FailAfterDeleteCatalogTypeStorage>();
                services.AddScoped<ICatalogTypeStorage>(sp => sp.GetRequiredService<FailAfterDeleteCatalogTypeStorage>());
            });

        Guid catalogId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync(CatalogCode);
        }

        // Act (external transaction)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: false);
            await uow.CommitAsync();
        }

        // Assert
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

    private static async Task EnsureTypedTableExistsAndEmptyAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var ddl = $"""
CREATE TABLE IF NOT EXISTS {TypedTable} (
    catalog_id UUID PRIMARY KEY REFERENCES catalogs(id) ON DELETE CASCADE,
    name TEXT NOT NULL DEFAULT '',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
TRUNCATE TABLE {TypedTable};
""";

        await conn.ExecuteAsync(ddl);
    }

    sealed class FailAfterInsertCatalogTypeStorage(IUnitOfWork uow) : ICatalogTypeStorage
    {
        public string CatalogCode => CatalogDraftService_ExternalTransactionMode_TypedStorageFailure_RollsBack_P0Tests.CatalogCode;

        public async Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default)
        {
            uow.EnsureActiveTransaction();

            var sql = $"INSERT INTO {TypedTable} (catalog_id, name) VALUES (@catalogId, 'test') ON CONFLICT (catalog_id) DO NOTHING;";
            await uow.Connection.ExecuteAsync(new CommandDefinition(sql, new { catalogId }, uow.Transaction, cancellationToken: ct));

            throw new NotSupportedException("simulated catalog storage failure (external tx)");
        }

        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    sealed class FailAfterDeleteCatalogTypeStorage(IUnitOfWork uow) : ICatalogTypeStorage
    {
        public string CatalogCode => CatalogDraftService_ExternalTransactionMode_TypedStorageFailure_RollsBack_P0Tests.CatalogCode;

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

            throw new NotSupportedException("simulated catalog delete failure (external tx)");
        }
    }
}
