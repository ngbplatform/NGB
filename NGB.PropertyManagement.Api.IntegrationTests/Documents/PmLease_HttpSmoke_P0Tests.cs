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

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmLease_HttpSmoke_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    // ASP.NET Core API is configured with JsonStringEnumConverter (see NGB.Api).
    // HttpClient JSON helpers use default options without enum-string support,
    // so tests must opt-in to the same enum serialization contract.
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmLease_HttpSmoke_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

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

        // Precondition: create required lookup entities
        var party = await CreatePartyAsync(client, "John Smith");
        var property = await CreatePropertyAsync(client, "101 Main St");

        property.Display.Should().NotBeNullOrWhiteSpace();
        var propertyDisplay = property.Display!;

        // 1) Metadata endpoint returns 200
        using (var metaResp = await client.GetAsync($"/api/documents/{PropertyManagementCodes.Lease}/metadata"))
        {
            metaResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var meta = await metaResp.Content.ReadFromJsonAsync<DocumentTypeMetadataDto>(Json);
            meta.Should().NotBeNull();
            meta!.DocumentType.Should().Be(PropertyManagementCodes.Lease);

            // Lease UI labels must be user-friendly (no technical suffixes).
            meta.List.Should().NotBeNull();
            meta.List!.Columns.Should().Contain(c => c.Key == "property_id" && c.Label == "Property");
            meta.List!.Columns.Should().Contain(c => c.Key == "start_on_utc" && c.Label == "Start On");
            meta.List!.Columns.Should().Contain(c => c.Key == "end_on_utc" && c.Label == "End On");

            meta.Parts.Should().NotBeNull();
            meta.Parts!.Should().ContainSingle(p => p.PartCode == "parties");

            var tenants = meta.Parts!.Single(p => p.PartCode == "parties");
            tenants.List.Columns.Should().Contain(c => c.Key == "party_id" && c.Label == "Party");
            tenants.List.Columns.Should().Contain(c => c.Key == "role" && c.Label == "Role");
            tenants.List.Columns.Should().Contain(c => c.Key == "is_primary" && c.Label == "Is Primary");
            tenants.List.Columns.Should().Contain(c => c.Key == "ordinal" && c.Label == "Ordinal");

        }

        // 2) Create (display is computed in DB)
        var createResp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = property.Id,
                    start_on_utc = "2026-02-01",
                    end_on_utc = "2027-01-31",
                    rent_amount = 1250.00m,
                    due_day = 5,
                    memo = "Initial lease (draft)"
                },
                parts = new
                {
                    parties = new
                    {
                        rows = new object[]
                        {
                            new
                            {
                                party_id = party.Id,
                                role = "PrimaryTenant",
                                is_primary = true,
                                ordinal = 1
                            }
                        }
                    }
                }
            });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        created.Should().NotBeNull();
        created!.Id.Should().NotBe(Guid.Empty);
        created.Status.Should().Be(DocumentStatus.Draft);
	    created.Payload.Fields!["display"].GetString().Should().Be($"{propertyDisplay} — 02/01/2026 → 01/31/2027");

        // 3) Update (full). Attempting to override display must be ignored by DB trigger.
        var updateResp = await client.PutAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}/{created.Id}",
            new
            {
                fields = new
                {
                    display = "Manual override",
                    property_id = property.Id,
                    start_on_utc = "2026-02-01",
                    end_on_utc = "2027-02-28",
                    rent_amount = 1300.00m,
                    due_day = 5,
                    memo = "Updated memo"
                },
                parts = new
                {
                    parties = new
                    {
                        rows = new object[]
                        {
                            new
                            {
                                party_id = party.Id,
                                role = "PrimaryTenant",
                                is_primary = true,
                                ordinal = 1
                            }
                        }
                    }
                }
            });

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        updated.Should().NotBeNull();
        updated!.Id.Should().Be(created.Id);
	    updated.Payload.Fields!["display"].GetString().Should().Be($"{propertyDisplay} — 02/01/2026 → 02/28/2027");

        // 4) Search
        var page = await client.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            $"/api/documents/{PropertyManagementCodes.Lease}?search=Main&offset=0&limit=50",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().Contain(i => i.Id == created.Id);
    }

    [Fact]
    public async Task Create_WhenStartOnIsMissing_ReturnsFriendlyRequiredMessage()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreatePartyAsync(client, "John Smith");
        var property = await CreatePropertyAsync(client, "101 Main St");

        var resp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = property.Id,
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
                                party_id = party.Id,
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
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Start On is required.");
        doc.RootElement.GetProperty("error").GetProperty("errors").TryGetProperty("start_on_utc", out var startOnErrors).Should().BeTrue();
        startOnErrors.EnumerateArray().Select(x => x.GetString()).Should().Contain("Start On is required.");
        doc.RootElement.GetProperty("error").GetProperty("issues").EnumerateArray().ToArray().Should().Contain(i =>
            i.GetProperty("path").GetString() == "start_on_utc"
            && i.GetProperty("scope").GetString() == "field"
            && i.GetProperty("message").GetString() == "Start On is required.");
    }

    [Fact]
    public async Task Create_WhenStartOnIsInvalid_ReturnsFriendlyDateMessage()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreatePartyAsync(client, "John Smith");
        var property = await CreatePropertyAsync(client, "101 Main St");

        var resp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = property.Id,
                    start_on_utc = "not-a-date",
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
                                party_id = party.Id,
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
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Enter a valid date for Start On.");
        doc.RootElement.GetProperty("error").GetProperty("issues").EnumerateArray().ToArray().Should().Contain(i =>
            i.GetProperty("path").GetString() == "start_on_utc"
            && i.GetProperty("scope").GetString() == "field"
            && i.GetProperty("message").GetString() == "Enter a valid date for Start On.");
    }

    [Fact]
    public async Task Create_WhenPartRowContainsDocumentId_ReturnsFriendlyPartMessage()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreatePartyAsync(client, "John Smith");
        var property = await CreatePropertyAsync(client, "101 Main St");

        var resp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = property.Id,
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
                                document_id = Guid.Empty,
                                party_id = party.Id,
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
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Document Id is managed automatically and cannot be set in Parties row 1.");
        doc.RootElement.GetProperty("error").GetProperty("issues").EnumerateArray().ToArray().Should().Contain(i =>
            i.GetProperty("path").GetString() == "parties[0].document_id"
            && i.GetProperty("scope").GetString() == "field"
            && i.GetProperty("message").GetString() == "Document Id is managed automatically and cannot be set in Parties row 1.");
    }

    [Fact]
    public async Task Create_WhenPartRowContainsUnknownField_ReturnsFriendlyPartMessage()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreatePartyAsync(client, "John Smith");
        var property = await CreatePropertyAsync(client, "101 Main St");

        var resp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = property.Id,
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
                                party_id = party.Id,
                                role = "PrimaryTenant",
                                is_primary = true,
                                ordinal = 1,
                                bogus_code = "x"
                            }
                        }
                    }
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("detail").GetString().Should().Be("Field 'Bogus Code' is not available in Parties row 1.");
        doc.RootElement.GetProperty("error").GetProperty("issues").EnumerateArray().ToArray().Should().Contain(i =>
            i.GetProperty("path").GetString() == "parties[0].bogus_code"
            && i.GetProperty("scope").GetString() == "field"
            && i.GetProperty("message").GetString() == "Field 'Bogus Code' is not available in Parties row 1.");
    }

    [Fact]
    public async Task GetPage_WhenPeriodRangeIsProvided_FiltersByResolvedDocumentDateColumn()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var party = await CreatePartyAsync(client, "John Smith");
        var property = await CreatePropertyAsync(client, "101 Main St");

        async Task<DocumentDto> CreateLeaseAsync(string startOn, string? endOn)
        {
            var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.Lease}",
                new
                {
                    fields = new
                    {
                        property_id = property.Id,
                        start_on_utc = startOn,
                        end_on_utc = endOn,
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
                                    party_id = party.Id,
                                    role = "PrimaryTenant",
                                    is_primary = true,
                                    ordinal = 1
                                }
                            }
                        }
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var created = await resp.Content.ReadFromJsonAsync<DocumentDto>(Json);
            created.Should().NotBeNull();
            return created!;
        }

        var febLease = await CreateLeaseAsync("2026-02-01", "2026-12-31");
        var marLease = await CreateLeaseAsync("2026-03-15", "2027-03-14");

        var page = await client.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            $"/api/documents/{PropertyManagementCodes.Lease}?offset=0&limit=50&deleted=all&periodFrom=2026-03-01&periodTo=2026-03-31",
            Json);

        page.Should().NotBeNull();
        page!.Items.Select(x => x.Id).Should().Contain(marLease.Id);
        page.Items.Select(x => x.Id).Should().NotContain(febLease.Id);
    }

    [Fact]
    public async Task GetPage_WhenPeriodRangeIsInvalid_ReturnsBadRequest()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var resp = await client.GetAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}?offset=0&limit=50&periodFrom=2026-04-01&periodTo=2026-03-31");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("detail").GetString().Should().Be("'periodFrom' must be less than or equal to 'periodTo'.");
    }

    private static async Task<CatalogItemDto> CreatePartyAsync(HttpClient client, string display)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display,
                    email = "john.smith@example.com",
                    phone = "+1-201-555-0101"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        created!.Id.Should().NotBe(Guid.Empty);
        return created;
    }

	private static async Task<CatalogItemDto> CreatePropertyAsync(HttpClient client, string display)
	{
	    // Lease requires a Unit. Create Building + Unit and return Unit.
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
	    var building = await buildingResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
	    building.Should().NotBeNull();
	    building!.Id.Should().NotBe(Guid.Empty);

	    var unitResp = await client.PostAsJsonAsync(
	        $"/api/catalogs/{PropertyManagementCodes.Property}",
	        new
	        {
	            fields = new
	            {
	                kind = "Unit",
	                parent_property_id = building.Id,
	                unit_no = "101"
	            }
	        });

	    unitResp.StatusCode.Should().Be(HttpStatusCode.OK);
	    var unit = await unitResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
	    unit.Should().NotBeNull();
	    unit!.Id.Should().NotBe(Guid.Empty);
	    return unit;
	}
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
