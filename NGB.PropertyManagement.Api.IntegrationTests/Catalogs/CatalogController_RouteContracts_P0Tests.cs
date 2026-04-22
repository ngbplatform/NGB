using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Catalogs;

[Collection(PmIntegrationCollection.Name)]
public sealed class CatalogController_RouteContracts_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public CatalogController_RouteContracts_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metadata_And_SoftDelete_Routes_Work_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using (var metadataResponse = await client.GetAsync("/api/catalogs/metadata"))
        {
            metadataResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var metadata = await metadataResponse.Content.ReadFromJsonAsync<IReadOnlyList<CatalogTypeMetadataDto>>(Json);
            metadata.Should().NotBeNull();
            metadata!.Should().Contain(x => x.CatalogType == PropertyManagementCodes.Property);
            metadata.Should().Contain(x => x.CatalogType == PropertyManagementCodes.Party);
        }

        using (var typeMetadataResponse = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Property}/metadata"))
        {
            typeMetadataResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var metadata = await typeMetadataResponse.Content.ReadFromJsonAsync<CatalogTypeMetadataDto>(Json);
            metadata.Should().NotBeNull();
            metadata!.CatalogType.Should().Be(PropertyManagementCodes.Property);
        }

        var createResponse = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    display = "Soft Delete Building",
                    address_line1 = "1 Soft Delete Way",
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();

        using (var getResponse = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Property}/{created!.Id}"))
        {
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var item = await getResponse.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
            item.Should().NotBeNull();
            item!.IsMarkedForDeletion.Should().BeFalse();
        }

        using (var markResponse = await client.PostAsync($"/api/catalogs/{PropertyManagementCodes.Property}/{created.Id}/mark-for-deletion", content: null))
        {
            markResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var getMarkedResponse = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Property}/{created.Id}"))
        {
            getMarkedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var item = await getMarkedResponse.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
            item.Should().NotBeNull();
            item!.IsMarkedForDeletion.Should().BeTrue();
        }

        using (var unmarkResponse = await client.PostAsync($"/api/catalogs/{PropertyManagementCodes.Property}/{created.Id}/unmark-for-deletion", content: null))
        {
            unmarkResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var getUnmarkedResponse = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Property}/{created.Id}"))
        {
            getUnmarkedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var item = await getUnmarkedResponse.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
            item.Should().NotBeNull();
            item!.IsMarkedForDeletion.Should().BeFalse();
        }
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
