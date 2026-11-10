using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents;
using NGB.Runtime.Documents;
using Npgsql;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class DraftSaveBoundaryTests
{
    [Fact]
    public async Task Saving_a_draft_invalidates_an_older_action_version_but_save_itself_accepts_stale_values()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (path, originalVersion) = await CreateCompleteInspectionAsync(admin);
        var original = await admin.GetFromJsonAsync<DocumentDto>(path, Json);

        using var changed = await admin.PutAsJsonAsync(path, Payload(("result", "Changed by another editor")));
        await EnsureSuccessAsync(changed);
        var saved = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.True(saved!.DocumentVersion > originalVersion);
        using var staleAction = await ExecutePostAsync(admin, path, originalVersion, "stale-before-save");
        await AssertErrorAsync(staleAction, HttpStatusCode.Conflict, "document.version_conflict");
        await AssertNoEffectsAsync(admin, path);

        // Characterize the remaining 3.4 contract: PUT has no expected-version token.
        // Advancing the action version does not prevent a stale editor from overwriting a draft.
        using var staleSave = await admin.PutAsJsonAsync(path, original!.Payload);
        await EnsureSuccessAsync(staleSave);
        var overwritten = await admin.GetFromJsonAsync<DocumentDto>(path, Json);
        Assert.Equal(original.Payload.Fields!["result"].GetString(), overwritten!.Payload.Fields!["result"].GetString());
        var current = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.True(current!.DocumentVersion > saved.DocumentVersion);
        using var post = await ExecutePostAsync(admin, path, current.DocumentVersion, "fresh-after-save");
        Assert.Equal(DocumentStatus.Posted, (await ReadActionAsync(post)).Document.Status);
    }

    [Fact]
    public async Task Generic_catalog_save_still_accepts_a_stale_payload_without_a_version_token()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var original = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/" + EquipmentDefinitions.EquipmentCode,
            Payload(("display", "Original equipment name")));
        var path = "/api/catalogs/" + EquipmentDefinitions.EquipmentCode + "/" + original.Id;
        using var renamed = await admin.PutAsJsonAsync(path, Payload(("display", "Changed by another editor")));
        await EnsureSuccessAsync(renamed);
        Assert.Equal("Changed by another editor", (await admin.GetFromJsonAsync<CatalogItemDto>(path, Json))!.Display);

        using var staleSave = await admin.PutAsJsonAsync(path, original.Payload);
        await EnsureSuccessAsync(staleSave);
        Assert.Equal(original.Display, (await admin.GetFromJsonAsync<CatalogItemDto>(path, Json))!.Display);
    }

    [Fact]
    public async Task Generic_payload_date_does_not_change_the_registry_date_without_an_explicit_header_update()
    {
        await using var host = new InspectionHost(
            configureServices: services => services.AddSingleton<IDefinitionsContributor, DatedDocumentDefinitions>(),
            prepareDatabase: async connectionString =>
            {
                Assert.Equal(0, await Migrator.Program.Main(["--connection=" + connectionString]));
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    CREATE TABLE doc_example_date_boundary (
                        document_id uuid PRIMARY KEY REFERENCES documents(id),
                        display text,
                        document_date_utc timestamptz NOT NULL
                    );
                    """, connection);
                await command.ExecuteNonQueryAsync();
            });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var date = new DateTime(2001, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var document = await PostAsync<DocumentDto>(admin, "/api/documents/" + DatedDocumentDefinitions.Code,
            Payload(("document_date_utc", date)));
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var registryDate = (await repository.GetAsync(document.Id))!.DateUtc;
        Assert.NotEqual(date, registryDate);
        var path = "/api/documents/" + DatedDocumentDefinitions.Code + "/" + document.Id;
        using var changed = await admin.PutAsJsonAsync(path, Payload(("document_date_utc", date.AddDays(1))));
        await EnsureSuccessAsync(changed);
        var saved = await admin.GetFromJsonAsync<DocumentDto>(path, Json);
        Assert.Equal(registryDate, (await repository.GetAsync(document.Id))!.DateUtc);
        Assert.Equal(date.AddDays(1), saved!.Payload.Fields!["document_date_utc"].GetDateTime());

        await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>()
            .UpdateDraftAsync(document.Id, number: null, dateUtc: date.AddDays(1));
        var synchronized = await admin.GetFromJsonAsync<DocumentDto>(path, Json);
        Assert.Equal(date.AddDays(1), (await repository.GetAsync(document.Id))!.DateUtc);
        Assert.Equal(document.Number, synchronized!.Number);
    }

    private sealed class DatedDocumentDefinitions : IDefinitionsContributor
    {
        public const string Code = "example.date-boundary";

        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(Code, document => document.Metadata(new DocumentTypeMetadata(Code,
                [new DocumentTableMetadata("doc_example_date_boundary", TableKind.Head,
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String),
                        new("document_date_utc", ColumnType.DateTimeUtc, Required: true)
                    ])])));
        }
    }
}
