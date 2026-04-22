using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

[Collection(PostgresCollection.Name)]
public sealed class CatalogService_TransactionalAndConfiguration_P5Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AtomicCatalogCode = "it_cat_atomic";
    private const string AtomicHeadTable = "cat_it_cat_atomic";
    private const string AtomicDisplayColumn = "name";

    private const string BadNoHeadCatalogCode = "it_cat_bad_no_head";
    private const string BadEmptyDisplayCatalogCode = "it_cat_bad_empty_display";

    [Fact]
    public async Task CreateAsync_WhenHeadWriteFails_RollsBackHeaderRow()
    {
        await EnsureAtomicHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        // DB-level failure (varchar(5) constraint) should roll back the whole transaction
        // including the common header row inserted into 'catalogs'.
        await FluentActions.Awaiting(() => svc.CreateAsync(
                AtomicCatalogCode,
                new RecordPayload(new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["name"] = System.Text.Json.JsonSerializer.SerializeToElement("TOO_LONG")
                }),
                CancellationToken.None))
            .Should()
            .ThrowAsync<PostgresException>()
            .WithMessage("*value too long*");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var headerCount = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM catalogs WHERE catalog_code = @code;",
            new { code = AtomicCatalogCode });

        headerCount.Should().Be(0);

        var headCount = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {AtomicHeadTable};");
        headCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateAsync_WhenHeadWriteFails_RollsBackAndDoesNotTouchUpdatedAtUtc()
    {
        await EnsureAtomicHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(
            AtomicCatalogCode,
            new RecordPayload(new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["name"] = System.Text.Json.JsonSerializer.SerializeToElement("ABCDE")
            }),
            CancellationToken.None);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var before = await conn.ExecuteScalarAsync<DateTime>(
            "SELECT updated_at_utc FROM catalogs WHERE id = @id;",
            new { id = created.Id });

        // Failing update (varchar length) should roll back:
        // - head row should not change
        // - catalogs.updated_at_utc should not change (TouchAsync is after the head UPSERT)
        await FluentActions.Awaiting(() => svc.UpdateAsync(
                AtomicCatalogCode,
                created.Id,
                new RecordPayload(new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["name"] = System.Text.Json.JsonSerializer.SerializeToElement("TOO_LONG")
                }),
                CancellationToken.None))
            .Should()
            .ThrowAsync<PostgresException>()
            .WithMessage("*value too long*");

        var after = await conn.ExecuteScalarAsync<DateTime>(
            "SELECT updated_at_utc FROM catalogs WHERE id = @id;",
            new { id = created.Id });

        after.Should().Be(before);

        var read = await svc.GetByIdAsync(AtomicCatalogCode, created.Id, CancellationToken.None);
        read.Payload.Fields!["name"].GetString().Should().Be("ABCDE");
    }

    [Fact]
    public async Task GetTypeMetadataAsync_WhenCatalogMetadataIsInvalid_ThrowsConfigurationViolation()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, BadCatalogContributor>());

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await FluentActions.Awaiting(() => svc.GetTypeMetadataAsync(BadNoHeadCatalogCode, CancellationToken.None))
            .Should()
            .ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*no Head table metadata*");

        await FluentActions.Awaiting(() => svc.GetTypeMetadataAsync(BadEmptyDisplayCatalogCode, CancellationToken.None))
            .Should()
            .ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*Presentation.DisplayColumn*");
    }

    private static async Task EnsureAtomicHeadTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Use varchar(5) to force a DB-level failure that bypasses service-level validation.
        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {AtomicHeadTable} (
                      catalog_id uuid PRIMARY KEY,
                      name       varchar(5) NOT NULL,

                      CONSTRAINT fk_{AtomicHeadTable}__catalog
                          FOREIGN KEY (catalog_id) REFERENCES catalogs(id)
                          ON DELETE CASCADE
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private IHost CreateHost()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, AtomicCatalogContributor>());

    private sealed class AtomicCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(AtomicCatalogCode, b => b.Metadata(new CatalogTypeMetadata(
                CatalogCode: AtomicCatalogCode,
                DisplayName: "IT Catalog Atomic",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: AtomicHeadTable,
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            // NOTE: MaxLength is not enforced by the service yet; DB constraint is stricter.
                            new("name", ColumnType.String, Required: true, MaxLength: 200),
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata(AtomicHeadTable, AtomicDisplayColumn),
                Version: new CatalogMetadataVersion(1, "integration-tests"))));
        }
    }

    private sealed class BadCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(BadNoHeadCatalogCode, b => b.Metadata(new CatalogTypeMetadata(
                CatalogCode: BadNoHeadCatalogCode,
                DisplayName: "Bad Catalog (No Head)",
                Tables: [],
                Presentation: new CatalogPresentationMetadata("", "name"),
                Version: new CatalogMetadataVersion(1, "integration-tests"))));

            builder.AddCatalog(BadEmptyDisplayCatalogCode, b => b.Metadata(new CatalogTypeMetadata(
                CatalogCode: BadEmptyDisplayCatalogCode,
                DisplayName: "Bad Catalog (Empty Display)",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: "cat_it_bad_empty_display",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            new("name", ColumnType.String, Required: true, MaxLength: 10),
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata("cat_it_bad_empty_display", DisplayColumn: ""),
                Version: new CatalogMetadataVersion(1, "integration-tests"))));
        }
    }
}
