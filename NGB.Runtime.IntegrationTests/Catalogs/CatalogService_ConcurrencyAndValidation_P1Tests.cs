using System.Text.Json;
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
public sealed class CatalogService_ConcurrencyAndValidation_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_universal";
    private const string OtherCatalogCode = "it_cat_other";
    private const string HeadTable = "cat_it_cat_universal";
    private const string DisplayColumn = "name";

    [Fact]
    public async Task CreateAsync_ConcurrentCreates_DoNotThrow_AndAllAreVisible()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();

        var tasks = Enumerable.Range(0, 20)
            .Select(async i =>
            {
                await using var scope = host.Services.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

                await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonSerializer.SerializeToElement($"U{i:000}")
                }), CancellationToken.None);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        await using var finalScope = host.Services.CreateAsyncScope();
        var finalSvc = finalScope.ServiceProvider.GetRequiredService<ICatalogService>();

        var page = await finalSvc.GetPageAsync(CatalogCode, new PageRequestDto(Offset: 0, Limit: 200), CancellationToken.None);
        page.Total.Should().Be(20);
        page.Items.Should().HaveCount(20);
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentUpdates_SerializeViaRowLock_AndKeepRequiredValues()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("John"),
            ["email"] = JsonSerializer.SerializeToElement("a@a.com")
        }), CancellationToken.None);

        var tasks = Enumerable.Range(0, 10)
            .Select(async i =>
            {
                await using var s = host.Services.CreateAsyncScope();
                var sc = s.ServiceProvider.GetRequiredService<ICatalogService>();

                await sc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(new Dictionary<string, JsonElement>
                {
                    ["email"] = JsonSerializer.SerializeToElement($"u{i}@example.com")
                }), CancellationToken.None);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        var read = await svc.GetByIdAsync(CatalogCode, created.Id, CancellationToken.None);
        read.Payload.Fields!["name"].GetString().Should().Be("John");

        var email = read.Payload.Fields!["email"].GetString();
        email.Should().NotBeNullOrWhiteSpace();
        email.Should().MatchRegex("^u\\d+@example\\.com$");
    }

    [Fact]
    public async Task UpdateAsync_RejectsNullForRequiredField()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("John")
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => svc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement((string?)null)
            }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*required*");
    }

    [Fact]
    public async Task CreateAsync_DateTimeUtc_RejectsNonUtc()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await FluentActions.Awaiting(() => svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("John"),
                // No trailing 'Z' / offset => RoundtripKind parses as Unspecified/Local, EnsureUtc rejects.
                ["last_contacted_at_utc"] = JsonSerializer.SerializeToElement("2026-02-21T00:00:00")
            }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*Enter a valid date and time for Last Contacted At.*");
    }

    [Fact]
    public async Task UpdateAsync_WhenCatalogBelongsToDifferentType_FailsFast()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("John")
        }), CancellationToken.None);

        await FluentActions.Awaiting(() => svc.UpdateAsync(OtherCatalogCode, created.Id, new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["email"] = JsonSerializer.SerializeToElement("x@y.com")
            }),
            CancellationToken.None))
            .Should()
            .ThrowAsync<NgbArgumentInvalidException>()
            .WithMessage("*belongs to*");
    }

    private static async Task EnsureHeadTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {HeadTable} (
                      catalog_id             uuid PRIMARY KEY,
                      name                   text NOT NULL,
                      email                  text NULL,
                      age                    int NULL,
                      rent_amount            numeric(18,2) NULL,
                      is_active              boolean NULL,
                      ref_id                 uuid NULL,
                      move_in_date           date NULL,
                      last_contacted_at_utc  timestamptz NULL,
                      extra_json             jsonb NULL,

                      CONSTRAINT fk_{HeadTable}__catalog
                          FOREIGN KEY (catalog_id) REFERENCES catalogs(id)
                          ON DELETE CASCADE
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private IHost CreateHost()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, UniversalCatalogContributor>());

    private sealed class UniversalCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(CatalogCode, b => b.Metadata(new CatalogTypeMetadata(
                CatalogCode: CatalogCode,
                DisplayName: "IT Catalog Universal",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: HeadTable,
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            new("name", ColumnType.String, Required: true, MaxLength: 200),
                            new("email", ColumnType.String, MaxLength: 200),
                            new("age", ColumnType.Int32),
                            new("rent_amount", ColumnType.Decimal),
                            new("is_active", ColumnType.Boolean),
                            new("ref_id", ColumnType.Guid),
                            new("move_in_date", ColumnType.Date),
                            new("last_contacted_at_utc", ColumnType.DateTimeUtc),
                            new("extra_json", ColumnType.Json),
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata(HeadTable, DisplayColumn),
                Version: new CatalogMetadataVersion(1, "integration-tests"))));

            // Second catalog type is only used for the "belongs to different type" guard test.
            builder.AddCatalog(OtherCatalogCode, b => b.Metadata(new CatalogTypeMetadata(
                CatalogCode: OtherCatalogCode,
                DisplayName: "IT Catalog Other",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: "cat_it_cat_other",
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            new("name", ColumnType.String, Required: true)
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata("cat_it_cat_other", "name"),
                Version: new CatalogMetadataVersion(1, "integration-tests"))));
        }
    }
}
