using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Catalogs;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmProperty_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    // ASP.NET Core API is configured with JsonStringEnumConverter (see NGB.Api).
    // HttpClient JSON helpers use default options without enum-string support,
    // so tests must opt-in to the same enum serialization contract.
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmProperty_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metadata_Create_Update_Search_Works_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // PM API enables HTTPS redirection. Using https scheme avoids redirects in TestServer.
            BaseAddress = new Uri("https://localhost")
        });

        // 1) Metadata endpoint returns 200
        using (var metaResp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Property}/metadata"))
        {
            metaResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var meta = await metaResp.Content.ReadFromJsonAsync<CatalogTypeMetadataDto>(Json);
            meta.Should().NotBeNull();
            meta!.CatalogType.Should().Be(PropertyManagementCodes.Property);
        }

        // 2) Create
        var createResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    display = "Sunset Plaza",
                    address_line1 = "123 Main St",
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        created!.Id.Should().NotBe(Guid.Empty);
        created.Display.Should().Be("Sunset Plaza");

        // 3) Update (partial)
        var updateResp = await client.PutAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}/{created.Id}",
            new
            {
                fields = new
                {
                    display = "Sunset Plaza (Updated)"
                }
            });

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        updated.Should().NotBeNull();
        updated!.Display.Should().Be("Sunset Plaza (Updated)");

        // 4) Search
        var page = await client.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(
            $"/api/catalogs/{PropertyManagementCodes.Property}?search=Updated&offset=0&limit=50",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().Contain(i => i.Id == created.Id);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
