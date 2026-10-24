using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Common;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(SchemaPostgresCollection.Name)]
public sealed class BoundedRecordReadScopeTests(SchemaPostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_bounded_record";

    [Theory]
    [InlineData(20, 10, 262144, false)]
    [InlineData(0, 10, 1024, true)]
    public async Task RejectsOversizedRowsOrTextBeforeDetailMaterialization(int rows, int maximumRows, int maximumBytes, bool largeText)
    {
        await CreateTablesAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, BoundedCatalogContributor>());
        await using var scope = host.Services.CreateAsyncScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var item = await catalogs.CreateAsync(CatalogCode,
            new RecordPayload(new Dictionary<string, JsonElement> { ["display"] = JsonSerializer.SerializeToElement("Small") }), default);
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            // Provider-level stress fixture: bypass normal input limits deliberately.
            await connection.ExecuteAsync("""
                INSERT INTO cat_it_bounded_record__rows(catalog_id, ordinal, value)
                SELECT @id, ordinal, 'value' FROM generate_series(1, @rows) ordinal;
                UPDATE cat_it_bounded_record SET display = @display WHERE catalog_id = @id;
                """, new { id = item.Id, rows, display = largeText ? new string('x', 100_000) : "Small" });
        }

        var reads = scope.ServiceProvider.GetRequiredService<IRecordReadScopeFactory>();
        await Assert.ThrowsAsync<RecordReadLimitExceededException>(() => reads.OpenAsync(
            new BusinessObjectRef(BusinessObjectKind.CatalogItem, CatalogCode, item.Id), maximumRows, maximumBytes, default));
        Assert.False(scope.ServiceProvider.GetRequiredService<IUnitOfWork>().HasActiveTransaction);
    }

    [Fact]
    public async Task GuardAndDetailReadUseOneReadOnlySnapshotAndReleaseIt()
    {
        await CreateTablesAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, BoundedCatalogContributor>());
        await using var scope = host.Services.CreateAsyncScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var item = await catalogs.CreateAsync(CatalogCode,
            new RecordPayload(new Dictionary<string, JsonElement> { ["display"] = JsonSerializer.SerializeToElement("Before") }), default);
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reads = scope.ServiceProvider.GetRequiredService<IRecordReadScopeFactory>();
        await using (await reads.OpenAsync(new BusinessObjectRef(BusinessObjectKind.CatalogItem, CatalogCode, item.Id), 10, 262144, default))
        {
            Assert.Equal("on", await uow.Connection.ExecuteScalarAsync<string>("SHOW transaction_read_only", transaction: uow.Transaction));
            Assert.Equal("repeatable read", await uow.Connection.ExecuteScalarAsync<string>("SHOW transaction_isolation", transaction: uow.Transaction));
            await using var writer = new NpgsqlConnection(Fixture.ConnectionString);
            await writer.ExecuteAsync("UPDATE cat_it_bounded_record SET display = 'After' WHERE catalog_id = @id", new { id = item.Id });
            Assert.Equal("Before", (await catalogs.GetByIdAsync(CatalogCode, item.Id, default)).Display);
        }
        Assert.False(uow.HasActiveTransaction);
        Assert.Equal("After", (await catalogs.GetByIdAsync(CatalogCode, item.Id, default)).Display);
    }

    [Theory]
    [InlineData(10_001, false, 10_003, 8 * 1024 * 1024)]
    [InlineData(0, true, 2, 8 * 1024 * 1024)]
    [InlineData(0, false, int.MaxValue, int.MaxValue)]
    public async Task CallerBudgetsAboveTheFormerMcpCeilingsWorkInPostgres(
        int rows, bool largeText, int maximumRows, int maximumBytes)
    {
        await CreateTablesAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, BoundedCatalogContributor>());
        await using var scope = host.Services.CreateAsyncScope();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var item = await catalogs.CreateAsync(CatalogCode,
            new RecordPayload(new Dictionary<string, JsonElement> { ["display"] = JsonSerializer.SerializeToElement("Small") }), default);
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            // Exercise storage reads independently of the existing write-payload limits.
            await connection.ExecuteAsync("""
                INSERT INTO cat_it_bounded_record__rows(catalog_id, ordinal, value)
                SELECT @id, ordinal, 'value' FROM generate_series(1, @rows) ordinal;
                UPDATE cat_it_bounded_record SET display = @display WHERE catalog_id = @id;
                """, new { id = item.Id, rows, display = largeText ? new string('x', 4 * 1024 * 1024 + 1) : "Small" });
        }

        var reads = scope.ServiceProvider.GetRequiredService<IRecordReadScopeFactory>();
        await using (await reads.OpenAsync(new(BusinessObjectKind.CatalogItem, CatalogCode, item.Id), maximumRows, maximumBytes, default))
        {
            Assert.True(scope.ServiceProvider.GetRequiredService<IUnitOfWork>().HasActiveTransaction);
        }

        Assert.False(scope.ServiceProvider.GetRequiredService<IUnitOfWork>().HasActiveTransaction);
    }

    private async Task CreateTablesAsync()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS cat_it_bounded_record (
                catalog_id uuid PRIMARY KEY REFERENCES catalogs(id),
                display text NOT NULL
            );
            CREATE TABLE IF NOT EXISTS cat_it_bounded_record__rows (
                catalog_id uuid NOT NULL REFERENCES catalogs(id),
                ordinal integer NOT NULL,
                value text NOT NULL,
                PRIMARY KEY(catalog_id, ordinal)
            );
            """);
    }

    private sealed class BoundedCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
            => builder.AddCatalog(CatalogCode, catalog => catalog.Metadata(new CatalogTypeMetadata(
                CatalogCode, "Bounded test catalog",
                [
                    new("cat_it_bounded_record", TableKind.Head,
                        [new("catalog_id", ColumnType.Guid, Required: true), new("display", ColumnType.String, Required: true)], []),
                    new("cat_it_bounded_record__rows", TableKind.Part,
                        [new("catalog_id", ColumnType.Guid, Required: true), new("ordinal", ColumnType.Int32), new("value", ColumnType.String)], [], "rows")
                ],
                new CatalogPresentationMetadata("cat_it_bounded_record", "display"), new CatalogMetadataVersion(1, "tests"))));
    }
}
