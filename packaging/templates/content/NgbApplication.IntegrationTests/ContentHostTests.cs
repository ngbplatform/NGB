using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Common;
using NGB.Contracts.Features;
using NGB.Contracts.Notes;
using NGB.Contracts.Services;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.Catalogs;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace NgbApplication.IntegrationTests;

public sealed class ContentHostTests
{
    private const string Catalog = "test.content-parent";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Default_features_support_private_upload_download_and_notes_on_an_authorized_parent()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await DatabaseBootstrapper.InitializeAsync(postgres.GetConnectionString(), default);
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            // This parent exists only in the disposable test, not in the empty application.
            await using var command = new NpgsqlCommand("""
                CREATE TABLE cat_test_content_parent (
                    catalog_id uuid PRIMARY KEY REFERENCES catalogs(id),
                    display text NOT NULL CHECK (char_length(display) <= 200)
                );
                """, connection);
            await command.ExecuteNonQueryAsync();
        }

        var accessKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var storage = new ContainerBuilder("ghcr.io/coollabsio/minio:RELEASE.2025-10-15T17-29-55Z"
                + "@sha256:69b55a1c1c5dc285ce04db96689f5b2102317fc77a50680a1874ca6efd1c87f9")
            .WithPortBinding(9000, true)
            .WithEnvironment("MINIO_ROOT_USER", "test-root")
            .WithEnvironment("MINIO_ROOT_PASSWORD", Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
            .WithEnvironment("MINIO_ENDPOINT", "http://localhost:9000")
            .WithEnvironment("MINIO_BUCKET", "ngb-attachments")
            .WithEnvironment("MINIO_APP_ACCESS_KEY", accessKey)
            .WithEnvironment("MINIO_APP_SECRET_KEY", secret)
            .WithEnvironment("MINIO_API_CORS_ALLOW_ORIGIN", "http://localhost:5182")
            .WithResourceMapping(Path.Combine(AppContext.BaseDirectory, "attachments.sh"), "/bootstrap/")
            .WithCommand("server", "/data")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(9000).ForPath("/minio/health/live")))
            .Build();
        await storage.StartAsync();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var provision = await storage.ExecAsync(["sh", "/bootstrap/attachments.sh"]);
            Assert.True(provision.ExitCode == 0,
                "Private storage initialization must be repeatable: " + provision.Stderr);
        }
        var endpoint = $"http://{storage.Hostname}:{storage.GetMappedPublicPort(9000)}";
        await using var identity = new TestIdentity();
        await identity.StartAsync();
        await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", postgres.GetConnectionString());
            builder.UseSetting("KeycloakSettings:Issuer", identity.Issuer);
            builder.UseSetting("KeycloakSettings:RequireHttpsMetadata", "false");
            builder.UseSetting("KeycloakSettings:ClientIds:0", TestIdentity.ClientId);
            builder.UseSetting("Attachments:MinIO:InternalEndpoint", endpoint);
            builder.UseSetting("Attachments:MinIO:PublicEndpoint", endpoint);
            builder.UseSetting("Attachments:MinIO:Bucket", "ngb-attachments");
            builder.UseSetting("Attachments:MinIO:AccessKey", accessKey);
            builder.UseSetting("Attachments:MinIO:SecretKey", secret);
            builder.UseSetting("Attachments:MinIO:AllowInsecureHttp", "true");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDefinitionsContributor, ParentDefinitions>();
                services.AddScoped<ICatalogTypeStorage>(provider => new PostgresHeadCatalogTypeStorage(
                    provider.GetRequiredService<IUnitOfWork>(), Catalog, "cat_test_content_parent",
                    [PostgresHeadCatalogTypeStorage.Column.DraftString("display", "display")]));
            });
        });
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await identity.TokenAsync("administrator"));
        var features = (await client.GetFromJsonAsync<FeatureStateDto[]>("/api/features", Json))!;
        Assert.True(Assert.Single(features, feature => feature.Code == "Attachments").Enabled);
        Assert.True(Assert.Single(features, feature => feature.Code == "Notes").Enabled);
        var parent = await PostAsync<CatalogItemDto>(client, "/api/catalogs/" + Catalog,
            new RecordPayload(new Dictionary<string, JsonElement> { ["display"] = JsonSerializer.SerializeToElement("Content test") }));
        var target = new BusinessObjectRef(BusinessObjectKind.CatalogItem, Catalog, parent.Id);
        var note = await PostAsync<NoteDto>(client, "/api/notes", new CreateNoteRequest(target, "Initial note"));
        using var updated = await client.PutAsJsonAsync("/api/notes/" + note.Id, new UpdateNoteRequest("Updated note", note.Version));
        updated.EnsureSuccessStatusCode();
        var changed = (await updated.Content.ReadFromJsonAsync<NoteDto>(Json))!;
        Assert.Equal("Updated note", changed.Text);

        var bytes = Encoding.UTF8.GetBytes("Private attachment content");
        var upload = await PostAsync<AttachmentUploadDto>(client, "/api/attachments/uploads",
            new CreateAttachmentUploadRequest(target, "example.txt", "text/plain", bytes.Length));
        using var raw = new HttpClient();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, upload.Url);
        preflight.Headers.Add("Origin", "http://localhost:5182");
        preflight.Headers.Add("Access-Control-Request-Method", "PUT");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
        using var cors = await raw.SendAsync(preflight);
        cors.EnsureSuccessStatusCode();
        Assert.Contains("http://localhost:5182", cors.Headers.GetValues("Access-Control-Allow-Origin"));
        using var put = new HttpRequestMessage(HttpMethod.Put, upload.Url) { Content = new ByteArrayContent(bytes) };
        foreach (var header in upload.Headers)
        {
            if (!put.Headers.TryAddWithoutValidation(header.Key, header.Value))
                put.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        using var uploaded = await raw.SendAsync(put);
        uploaded.EnsureSuccessStatusCode();
        using var completed = await client.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null);
        completed.EnsureSuccessStatusCode();
        using var download = await client.PostAsync($"/api/attachments/{upload.AttachmentId}/download", null);
        download.EnsureSuccessStatusCode();
        var signed = (await download.Content.ReadFromJsonAsync<AttachmentDownloadDto>(Json))!;
        Assert.Equal(bytes, await raw.GetByteArrayAsync(signed.Url));
        using var anonymous = await raw.GetAsync(new Uri(signed.Url).GetLeftPart(UriPartial.Path));
        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await identity.TokenAsync("reader"));
        using var denied = await client.PostAsync($"/api/attachments/{upload.AttachmentId}/download", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedNote = await client.PutAsJsonAsync("/api/notes/" + note.Id, new UpdateNoteRequest("Denied", changed.Version));
        Assert.Equal(HttpStatusCode.Forbidden, deniedNote.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await identity.TokenAsync("administrator"));
        using var deleted = await client.DeleteAsync($"/api/notes/{note.Id}?version={changed.Version}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private sealed class ParentDefinitions : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(Catalog, catalog => catalog.Metadata(new CatalogTypeMetadata(
                Catalog, "Test content parents",
                [
                    new("cat_test_content_parent", TableKind.Head,
                        [
                            new("catalog_id", ColumnType.Guid, Required: true),
                            new("display", ColumnType.String, Required: true, MaxLength: 200)
                        ], [])
                ],
                new("cat_test_content_parent", "display"), new(1, "test-content-parent"))));
        }
    }
}
