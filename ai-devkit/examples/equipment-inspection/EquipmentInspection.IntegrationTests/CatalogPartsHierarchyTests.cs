using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Universal;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class CatalogPartsHierarchyTests
{
    private const string Catalog = "example.inspection-groups";
    private const string Endpoint = "/api/catalogs/" + Catalog;

    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Catalog_parts_replace_explicitly_preserve_omitted_rows_and_rollback_with_the_head(string username)
    {
        await using var host = CreateHost();
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Application administrator");
        }
        using var admin = await host.CreateClientAsync(username);
        var original = await PostAsync<CatalogItemDto>(admin, Endpoint,
            WithRows("Initial group", (2, "Second"), (1, "First")));
        var path = Endpoint + "/" + original.Id;
        Assert.Equal(new[] { "First", "Second" }, Labels(original));

        using var renamed = await admin.PutAsJsonAsync(path, Payload(("display", "Renamed group")));
        await EnsureSuccessAsync(renamed);
        var preserved = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.Equal("Renamed group", preserved!.Display);
        Assert.Equal(Labels(original), Labels(preserved));

        using var conflict = await admin.PutAsJsonAsync(path, WithRows("Must roll back", (1, "Duplicate"), (1, "Duplicate")));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var unchanged = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.Equal(preserved.Display, unchanged!.Display);
        Assert.Equal(Labels(original), Labels(unchanged));

        using var replaced = await admin.PutAsJsonAsync(path, WithRows("Replacement", (3, "Only row")));
        await EnsureSuccessAsync(replaced);
        Assert.Equal(new[] { "Only row" }, Labels((await admin.GetFromJsonAsync<CatalogItemDto>(path, Json))!));
        using var reader = await host.CreateClientAsync("reader");
        using var deniedRead = await reader.GetAsync(path);
        using var deniedUpdate = await reader.PutAsJsonAsync(path, WithRows("Denied"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedUpdate.StatusCode);
        Assert.Equal(new[] { "Only row" }, Labels((await admin.GetFromJsonAsync<CatalogItemDto>(path, Json))!));

        using var cleared = await admin.PutAsJsonAsync(path, WithRows("Empty group"));
        await EnsureSuccessAsync(cleared);
        Assert.Empty(Labels((await admin.GetFromJsonAsync<CatalogItemDto>(path, Json))!));
    }

    [Fact]
    public async Task Trusted_hierarchy_queries_detect_cycles_and_depth_without_installing_a_parent_write_policy()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var root = await PostAsync<CatalogItemDto>(admin, Endpoint, Payload(("display", "Root")));
        var child = await PostAsync<CatalogItemDto>(admin, Endpoint, Payload(("display", "Child"), ("parent_id", root.Id)));
        var leaf = await PostAsync<CatalogItemDto>(admin, Endpoint, Payload(("display", "Leaf"), ("parent_id", child.Id)));
        var unrelated = await PostAsync<CatalogItemDto>(admin, Endpoint, Payload(("display", "Unrelated")));
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<ICatalogReader>();
        var metadata = GroupDefinitions.Metadata;
        var table = metadata.Tables.Single(item => item.Kind == TableKind.Head);
        var head = new CatalogHeadDescriptor(Catalog, table.TableName, metadata.Presentation.DisplayColumn,
            table.Columns.Where(column => column.ColumnName != "catalog_id")
                .Select(column => new CatalogHeadColumn(column.ColumnName, column.ColumnType)).ToArray());

        Assert.Equal(new[] { child.Id, leaf.Id }.Order(),
            (await reader.GetActiveDescendantIdsAsync(head, [root.Id, root.Id], "parent_id")).Order());
        Assert.Empty(await reader.GetActiveDescendantIdsAsync(head, [unrelated.Id], "parent_id"));
        Assert.Empty(await reader.GetActiveDescendantIdsAsync(head, [], "parent_id"));
        Assert.True(await reader.HasParentChainViolationAsync(head, root.Id, leaf.Id, "parent_id", 8));
        Assert.True(await reader.HasParentChainViolationAsync(head, root.Id, root.Id, "parent_id", 8));
        Assert.True(await reader.HasParentChainViolationAsync(head, unrelated.Id, leaf.Id, "parent_id", 2));
        Assert.False(await reader.HasParentChainViolationAsync(head, unrelated.Id, leaf.Id, "parent_id", 8));
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            reader.GetActiveDescendantIdsAsync(head, [root.Id], "unknown_parent"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.GetActiveDescendantIdsAsync(head, [root.Id], "parent_id", cancellation.Token));
    }

    private static InspectionHost CreateHost() => new(
        configureServices: services => services.AddSingleton<IDefinitionsContributor, GroupDefinitions>(),
        prepareDatabase: PrepareDatabaseAsync);

    private static async Task PrepareDatabaseAsync(string connectionString)
    {
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + connectionString]));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        // Test-only application tables; no existing platform schema is altered.
        await using var command = new NpgsqlCommand("""
            CREATE TABLE cat_example_inspection_groups (
                catalog_id uuid PRIMARY KEY REFERENCES catalogs(id),
                display varchar(200) NOT NULL,
                parent_id uuid REFERENCES cat_example_inspection_groups(catalog_id)
            );
            CREATE TABLE cat_example_inspection_groups__checks (
                catalog_id uuid NOT NULL REFERENCES cat_example_inspection_groups(catalog_id),
                ordinal integer NOT NULL,
                label varchar(200) NOT NULL,
                PRIMARY KEY (catalog_id, ordinal)
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static RecordPayload WithRows(string display, params (int Ordinal, string Label)[] rows)
        => new(Payload(("display", display)).Fields,
            new Dictionary<string, RecordPartPayload>
            {
                ["checks"] = new(rows.Select(row => Payload(("ordinal", row.Ordinal), ("label", row.Label)).Fields!).ToArray())
            });

    private static string?[] Labels(CatalogItemDto item)
        => item.Payload.Parts!["checks"].Rows.Select(row => row["label"].GetString()).ToArray();

    private sealed class GroupDefinitions : IDefinitionsContributor
    {
        public static readonly CatalogTypeMetadata Metadata = new(Catalog, "Inspection groups",
            [
                new("cat_example_inspection_groups", TableKind.Head,
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String, Required: true, MaxLength: 200),
                        new("parent_id", ColumnType.Guid, Lookup: new CatalogLookupSourceMetadata(Catalog))
                    ], []),
                new("cat_example_inspection_groups__checks", TableKind.Part,
                    [
                        new("catalog_id", ColumnType.Guid, Required: true),
                        new("ordinal", ColumnType.Int32, Required: true),
                        new("label", ColumnType.String, Required: true, MaxLength: 200)
                    ], [], PartCode: "checks")
            ],
            new("cat_example_inspection_groups", "display"), new(1, "catalog-parts-example"));

        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(Catalog, catalog => catalog.Metadata(Metadata));
        }
    }
}
