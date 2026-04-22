using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Core.AuditLog;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Catalogs;

/// <summary>
/// P7: CatalogService should preserve platform AuditLog semantics through CatalogDraftService.
/// These are end-to-end tests through Runtime -> Persistence (reader/writer/repo) -> PostgreSQL.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CatalogService_AuditLog_P7Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string CatalogCode = "it_cat_audit";
    private const string HeadTable = "cat_it_cat_audit";
    private const string PartTable = "cat_it_cat_audit__contacts";
    private const string DisplayColumn = "name";
    private const string PartCode = "contacts";

    [Fact]
    public async Task CreateAsync_WritesCatalogCreateAuditEvent_WithExpectedChanges()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("John Audit")
        }), CancellationToken.None);

        var events = await audit.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Catalog,
            EntityId: created.Id,
            Limit: 50,
            Offset: 0));

        var ev = events.Single(e => e.ActionCode == AuditActionCodes.CatalogCreate);

        ev.EntityKind.Should().Be(AuditEntityKind.Catalog);
        ev.EntityId.Should().Be(created.Id);

        ev.MetadataJson.Should().NotBeNull();
        ev.MetadataJson!.Should().Contain(CatalogCode);

        ev.Changes.Should().ContainEquivalentOf(new AuditFieldChange(
            FieldPath: "catalog_code",
            OldValueJson: null,
            NewValueJson: JsonSerializer.Serialize(CatalogCode)));

        ev.Changes.Should().ContainEquivalentOf(new AuditFieldChange(
            FieldPath: "is_deleted",
            OldValueJson: null,
            NewValueJson: "false"));

        ev.Changes.Should().ContainEquivalentOf(new AuditFieldChange(
            FieldPath: "name",
            OldValueJson: null,
            NewValueJson: JsonSerializer.Serialize("John Audit")));
    }

    [Fact]
    public async Task UpdateAsync_WritesCatalogUpdateAuditEvent_WithFieldDiffs()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Before Name"),
            ["email"] = JsonSerializer.SerializeToElement("before@example.com")
        }), CancellationToken.None);

        await svc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("After Name"),
            ["email"] = JsonSerializer.SerializeToElement("after@example.com")
        }), CancellationToken.None);

        var events = await audit.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Catalog,
            EntityId: created.Id,
            Limit: 50,
            Offset: 0));

        var ev = events.Single(e => e.ActionCode == AuditActionCodes.CatalogUpdate);
        ev.Changes.Should().ContainEquivalentOf(new AuditFieldChange(
            FieldPath: "name",
            OldValueJson: JsonSerializer.Serialize("Before Name"),
            NewValueJson: JsonSerializer.Serialize("After Name")));
        ev.Changes.Should().ContainEquivalentOf(new AuditFieldChange(
            FieldPath: "email",
            OldValueJson: JsonSerializer.Serialize("before@example.com"),
            NewValueJson: JsonSerializer.Serialize("after@example.com")));
    }

    [Fact]
    public async Task UpdateAsync_WhenPayloadDoesNotChangeBusinessFields_DoesNotWriteCatalogUpdateAuditEvent()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Same Name"),
            ["email"] = JsonSerializer.SerializeToElement("same@example.com")
        }), CancellationToken.None);

        await svc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Same Name"),
            ["email"] = JsonSerializer.SerializeToElement("same@example.com")
        }), CancellationToken.None);

        var events = await audit.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Catalog,
            EntityId: created.Id,
            Limit: 50,
            Offset: 0));

        events.Should().ContainSingle(e => e.ActionCode == AuditActionCodes.CatalogCreate);
        events.Should().NotContain(e => e.ActionCode == AuditActionCodes.CatalogUpdate);
    }

    [Fact]
    public async Task CreateAndUpdateAsync_WithParts_WritesFlattenedPartAuditChanges()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(
            Fields: new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("With Parts")
            },
            Parts: new Dictionary<string, RecordPartPayload>
            {
                [PartCode] = new RecordPartPayload(
                [
                    new Dictionary<string, JsonElement>
                    {
                        ["ordinal"] = JsonSerializer.SerializeToElement(1),
                        ["label"] = JsonSerializer.SerializeToElement("Home")
                    }
                ])
            }), CancellationToken.None);

        await svc.UpdateAsync(CatalogCode, created.Id, new RecordPayload(
            Parts: new Dictionary<string, RecordPartPayload>
            {
                [PartCode] = new RecordPartPayload(
                [
                    new Dictionary<string, JsonElement>
                    {
                        ["ordinal"] = JsonSerializer.SerializeToElement(1),
                        ["label"] = JsonSerializer.SerializeToElement("Primary")
                    }
                ])
            }), CancellationToken.None);

        var events = await audit.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Catalog,
            EntityId: created.Id,
            Limit: 50,
            Offset: 0));

        var create = events.Single(e => e.ActionCode == AuditActionCodes.CatalogCreate);
        create.Changes.Should().ContainEquivalentOf(new AuditFieldChange(
            FieldPath: "parts.contacts[1].label",
            OldValueJson: null,
            NewValueJson: JsonSerializer.Serialize("Home")));

        var update = events.Single(e => e.ActionCode == AuditActionCodes.CatalogUpdate);
        update.Changes.Should().ContainEquivalentOf(new AuditFieldChange(
            FieldPath: "parts.contacts[1].label",
            OldValueJson: JsonSerializer.Serialize("Home"),
            NewValueJson: JsonSerializer.Serialize("Primary")));
    }

    [Fact]
    public async Task MarkForDeletionAsync_IsIdempotent_AndWritesSingleAuditEvent()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("To Delete")
        }), CancellationToken.None);

        await svc.MarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None);
        await svc.MarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None); // no-op

        var events = await audit.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Catalog,
            EntityId: created.Id,
            Limit: 50,
            Offset: 0));

        events.Count(e => e.ActionCode == AuditActionCodes.CatalogMarkForDeletion).Should().Be(1);

        var mark = events.Single(e => e.ActionCode == AuditActionCodes.CatalogMarkForDeletion);
        mark.Changes.Should().ContainEquivalentOf(new AuditFieldChange(
            FieldPath: "is_deleted",
            OldValueJson: "false",
            NewValueJson: "true"));
    }

    [Fact]
    public async Task UnmarkForDeletionAsync_IsIdempotent_AndWritesSingleAuditEvent()
    {
        await EnsureHeadTableExistsAsync(Fixture.ConnectionString);

        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var created = await svc.CreateAsync(CatalogCode, new RecordPayload(new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement("Restore")
        }), CancellationToken.None);

        await svc.MarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None);

        await svc.UnmarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None);
        await svc.UnmarkForDeletionAsync(CatalogCode, created.Id, CancellationToken.None); // no-op

        var events = await audit.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Catalog,
            EntityId: created.Id,
            Limit: 50,
            Offset: 0));

        events.Count(e => e.ActionCode == AuditActionCodes.CatalogUnmarkForDeletion).Should().Be(1);

        var unmark = events.Single(e => e.ActionCode == AuditActionCodes.CatalogUnmarkForDeletion);
        unmark.Changes.Should().ContainEquivalentOf(new AuditFieldChange(
            FieldPath: "is_deleted",
            OldValueJson: "true",
            NewValueJson: "false"));
    }

    private static async Task EnsureHeadTableExistsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var sql = $"""
                  CREATE TABLE IF NOT EXISTS {HeadTable} (
                      catalog_id uuid PRIMARY KEY,
                      name       text NOT NULL,
                      email      text NULL,

                      CONSTRAINT fk_{HeadTable}__catalog
                          FOREIGN KEY (catalog_id) REFERENCES catalogs(id)
                          ON DELETE CASCADE
                  );

                  CREATE TABLE IF NOT EXISTS {PartTable} (
                      catalog_id uuid NOT NULL,
                      ordinal    int  NOT NULL,
                      label      text NOT NULL,

                      CONSTRAINT fk_{PartTable}__catalog
                          FOREIGN KEY (catalog_id) REFERENCES catalogs(id)
                          ON DELETE CASCADE,
                      CONSTRAINT ux_{PartTable}__catalog_ordinal
                          UNIQUE (catalog_id, ordinal)
                  );
                  """;

        await conn.ExecuteAsync(sql);
    }

    private IHost CreateHost()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, AuditCatalogContributor>());

    private sealed class AuditCatalogContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(CatalogCode, b => b.Metadata(new CatalogTypeMetadata(
                CatalogCode: CatalogCode,
                DisplayName: "IT Catalog Audit",
                Tables:
                [
                    new CatalogTableMetadata(
                        TableName: HeadTable,
                        Kind: TableKind.Head,
                        Columns:
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            new("name", ColumnType.String, Required: true, MaxLength: 200),
                            new("email", ColumnType.String, MaxLength: 200)
                        ],
                        Indexes: []),
                    new CatalogTableMetadata(
                        TableName: PartTable,
                        Kind: TableKind.Part,
                        PartCode: PartCode,
                        Columns:
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            new("ordinal", ColumnType.Int32, Required: true),
                            new("label", ColumnType.String, Required: true, MaxLength: 200)
                        ],
                        Indexes: [])
                ],
                Presentation: new CatalogPresentationMetadata(HeadTable, DisplayColumn),
                Version: new CatalogMetadataVersion(1, "integration-tests"))));
        }
    }
}
