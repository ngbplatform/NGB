using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmLease_PartyRoleValidation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmLease_PartyRoleValidation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_WhenLeasePartyIsVendorOnly_Returns400_WithFriendlyFieldError()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var vendor = await CreatePartyAsync(client, "Vendor Only", isTenant: false, isVendor: true);
        var unit = await CreatePropertyAsync(client, "101 Main St");

        using var resp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = unit.Id,
                    start_on_utc = "2026-02-01",
                    end_on_utc = "2027-01-31",
                    rent_amount = 1250.00m,
                    due_day = 5
                },
                parts = new
                {
                    parties = new
                    {
                        rows = new object[]
                        {
                            new
                            {
                                party_id = vendor.Id,
                                role = "PrimaryTenant",
                                is_primary = true,
                                ordinal = 1
                            }
                        }
                    }
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.lease.party.must_be_tenant");
        root.GetProperty("detail").GetString().Should().Be("Selected party must be marked as Tenant.");
        root.GetProperty("error").GetProperty("errors").GetProperty("parties[0].party_id").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("Selected party must be marked as Tenant.");
        root.GetProperty("error").GetProperty("issues").EnumerateArray().ToArray().Should().Contain(i =>
            i.GetProperty("path").GetString() == "parties[0].party_id"
            && i.GetProperty("scope").GetString() == "field"
            && i.GetProperty("message").GetString() == "Selected party must be marked as Tenant.");
    }

    private static async Task<CatalogItemDto> CreatePartyAsync(HttpClient client, string display, bool isTenant = true, bool isVendor = false)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display,
                    email = "party@example.com",
                    phone = "+1-201-555-0101",
                    is_tenant = isTenant,
                    is_vendor = isVendor
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>();
        created.Should().NotBeNull();
        return created!;
    }

    private static async Task<CatalogItemDto> CreatePropertyAsync(HttpClient client, string display)
    {
        var buildingResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    address_line1 = display,
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        buildingResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var building = await buildingResp.Content.ReadFromJsonAsync<CatalogItemDto>();
        building.Should().NotBeNull();

        var unitResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    parent_property_id = building!.Id,
                    unit_no = "101"
                }
            });

        unitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var unit = await unitResp.Content.ReadFromJsonAsync<CatalogItemDto>();
        unit.Should().NotBeNull();
        return unit!;
    }
}
