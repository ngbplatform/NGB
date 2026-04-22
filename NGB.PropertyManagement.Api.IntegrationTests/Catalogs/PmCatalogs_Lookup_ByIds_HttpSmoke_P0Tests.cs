using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Catalogs;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmCatalogs_Lookup_ByIds_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    // ASP.NET Core API is configured with JsonStringEnumConverter (see NGB.Api).
    // HttpClient JSON helpers use default options without enum-string support,
    // so tests must opt-in to the same enum serialization contract.
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmCatalogs_Lookup_ByIds_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Party_Lookup_And_ByIds_Works_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // PM API enables HTTPS redirection. Using https scheme avoids redirects in TestServer.
            BaseAddress = new Uri("https://localhost")
        });

        // Create two parties
        var a = await CreatePartyAsync(client, "John Smith", "john.smith@example.com");
        var b = await CreatePartyAsync(client, "Jane Smith", "jane.smith@example.com");

        // 1) lookup (q)
        using (var resp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Party}/lookup?q=Jane&limit=20"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var items = await resp.Content.ReadFromJsonAsync<IReadOnlyList<LookupItemDto>>(Json);
            items.Should().NotBeNull();
            items!.Should().Contain(x => x.Id == b.Id && x.Label.Contains("Jane"));
        }

        // 2) by-ids
        using (var resp = await client.PostAsJsonAsync(
                   $"/api/catalogs/{PropertyManagementCodes.Party}/by-ids",
                   new { ids = new[] { a.Id, b.Id } }))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var items = await resp.Content.ReadFromJsonAsync<IReadOnlyList<LookupItemDto>>(Json);
            items.Should().NotBeNull();
            items!.Select(x => x.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id });
        }
    }

    [Fact]
    public async Task Property_Lookup_And_ByIds_Works_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // PM API enables HTTPS redirection. Using https scheme avoids redirects in TestServer.
            BaseAddress = new Uri("https://localhost")
        });

        // Create two properties
        var a = await CreatePropertyAsync(client, "101 Main St");
        var b = await CreatePropertyAsync(client, "202 Broad Ave");

        // 1) lookup (q)
        using (var resp = await client.GetAsync($"/api/catalogs/{PropertyManagementCodes.Property}/lookup?q=Broad&limit=20"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var items = await resp.Content.ReadFromJsonAsync<IReadOnlyList<LookupItemDto>>(Json);
            items.Should().NotBeNull();
            items!.Should().Contain(x => x.Id == b.Id && x.Label.Contains("Broad"));
        }

        // 2) by-ids
        using (var resp = await client.PostAsJsonAsync(
                   $"/api/catalogs/{PropertyManagementCodes.Property}/by-ids",
                   new { ids = new[] { a.Id, b.Id } }))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var items = await resp.Content.ReadFromJsonAsync<IReadOnlyList<LookupItemDto>>(Json);
            items.Should().NotBeNull();
            items!.Select(x => x.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id });
        }
    }

    private static async Task<CatalogItemDto> CreatePartyAsync(HttpClient client, string display, string email)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display,
                    email,
                    phone = "+1-201-555-0101"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        created!.Id.Should().NotBe(Guid.Empty);
        created.Display.Should().Be(display);
        return created;
    }

    private static async Task<CatalogItemDto> CreatePropertyAsync(HttpClient client, string display)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    display,
                    address_line1 = display,
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        created!.Id.Should().NotBe(Guid.Empty);
        created.Display.Should().Be(display);
        return created;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
