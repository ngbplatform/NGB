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
public sealed class PmParty_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmParty_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metadata_Create_Update_Search_Works_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using (var metaResp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Party}/metadata"))
        {
            metaResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var meta = await metaResp.Content.ReadFromJsonAsync<CatalogTypeMetadataDto>(Json);
            meta.Should().NotBeNull();
            meta!.CatalogType.Should().Be(PropertyManagementCodes.Party);

            var fields = meta.Form!.Sections
                .SelectMany(x => x.Rows)
                .SelectMany(x => x.Fields)
                .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

            fields.Should().ContainKey("is_tenant");
            fields["is_tenant"].Label.Should().Be("Tenant");
            fields.Should().ContainKey("is_vendor");
            fields["is_vendor"].Label.Should().Be("Vendor");
        }

        var createResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display = "John Smith",
                    email = "john.smith@example.com",
                    phone = "+1-201-555-0101"
                }
            });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        var createdItem = created!;
        var createdFields = createdItem.Payload.Fields!;
        createdItem.Id.Should().NotBe(Guid.Empty);
        createdItem.Display.Should().Be("John Smith");
        createdFields.Should().ContainKey("is_tenant");
        createdFields["is_tenant"].GetBoolean().Should().BeTrue();
        createdFields.Should().ContainKey("is_vendor");
        createdFields["is_vendor"].GetBoolean().Should().BeFalse();

        var updateResp = await client.PutAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}/{createdItem.Id}",
            new
            {
                fields = new
                {
                    display = "John Smith (Updated)",
                    is_vendor = true
                }
            });

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        updated.Should().NotBeNull();
        var updatedItem = updated!;
        var updatedFields = updatedItem.Payload.Fields!;
        updatedItem.Display.Should().Be("John Smith (Updated)");
        updatedFields["is_tenant"].GetBoolean().Should().BeTrue();
        updatedFields["is_vendor"].GetBoolean().Should().BeTrue();

        var page = await client.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(
            $"/api/catalogs/{PropertyManagementCodes.Party}?search=Updated&offset=0&limit=50",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().Contain(i => i.Id == createdItem.Id);
    }

    [Fact]
    public async Task GetPage_WhenUnknownFilterProvided_ReturnsBadRequest()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var resp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Party}?offset=0&limit=50&foo=bar");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Filter 'Foo' is not available for this list.");
    }

    [Fact]
    public async Task GetPage_WhenDeletedFilterIsInvalid_ReturnsBadRequest()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var resp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Party}?offset=0&limit=50&deleted=nope");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Select Active, Deleted, or All.");
    }

    [Fact]
    public async Task Create_WhenUnknownFieldProvided_ReturnsBadRequest()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display = "John Smith",
                    foo = "bar"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Field 'Foo' is not available on this form.");
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
