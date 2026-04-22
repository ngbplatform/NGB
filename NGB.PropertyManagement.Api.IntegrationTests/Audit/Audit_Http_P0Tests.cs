using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Audit;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Audit;

[Collection(PmIntegrationCollection.Name)]
public sealed class Audit_Http_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public Audit_Http_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EntityEndpoint_Returns_Catalog_Audit_Items()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var createResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new { fields = new { display = "Audit Test Party", email = "audit@example.com" } });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();

        var page = await client.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/audit/entities/{(short)AuditEntityKind.Catalog}/{created!.Id}?limit=20",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().NotBeEmpty();
        page.Items.Should().Contain(x => x.EntityId == created.Id && x.ActionCode == "catalog.create");
    }

    [Fact]
    public async Task EntityEndpoint_Returns_Audit_Actor_From_Keycloak_User()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var createResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new { fields = new { display = "Actor Test Party", email = "actor@example.com" } });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();

        var page = await client.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/audit/entities/{(short)AuditEntityKind.Catalog}/{created!.Id}?limit=20",
            Json);

        page.Should().NotBeNull();

        var auditEvent = page!.Items.Single(x => x.EntityId == created.Id && x.ActionCode == "catalog.create");
        auditEvent.Actor.Should().NotBeNull();
        auditEvent.Actor!.UserId.Should().NotBeNull();
        auditEvent.Actor.Email.Should().Be("pm-admin@integration.test");
        auditEvent.Actor.DisplayName.Should().Be("PM Admin");
    }

    [Fact]
    public async Task EntityEndpoint_Returns_ChartOfAccounts_Audit_Items()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var createResp = await client.PostAsJsonAsync(
            "/api/chart-of-accounts",
            new { code = "AUD-1000", name = "Audit Cash", accountType = "Asset", isActive = true });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<NGB.Contracts.Admin.ChartOfAccountsAccountDto>(Json);
        created.Should().NotBeNull();

        var page = await client.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/audit/entities/{(short)AuditEntityKind.ChartOfAccountsAccount}/{created!.AccountId}?limit=20",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().NotBeEmpty();
        page.Items.Should().Contain(x => x.EntityId == created.AccountId && x.ActionCode == "coa.account.create");
    }

    [Fact]
    public async Task EntityEndpoint_Returns_Document_Audit_Items()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var tenant = await CreatePartyAsync(client, "Audit Tenant");
        var building = await CreateBuildingAsync(client);
        var unit = await CreateUnitAsync(client, building.Id, "101");

        var createResp = await client.PostAsJsonAsync(
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = unit.Id,
                    start_on_utc = "2026-02-01",
                    end_on_utc = "2027-01-31",
                    rent_amount = 1250.00m,
                    due_day = 5,
                    memo = "Audit lease"
                },
                parts = new
                {
                    parties = new
                    {
                        rows = new object[]
                        {
                            new { party_id = tenant.Id, role = "PrimaryTenant", is_primary = true, ordinal = 1 }
                        }
                    }
                }
            });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<DocumentDto>(Json);
        created.Should().NotBeNull();

        var page = await client.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/audit/entities/{(short)AuditEntityKind.Document}/{created!.Id}?limit=20",
            Json);

        page.Should().NotBeNull();
        page!.Items.Should().NotBeEmpty();
        page.Items.Should().Contain(x => x.EntityId == created.Id && x.ActionCode == "document.create_draft");
    }

    private static async Task<CatalogItemDto> CreatePartyAsync(HttpClient client, string display)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new { fields = new { display, email = "tenant@example.com" } });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private static async Task<CatalogItemDto> CreateBuildingAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    address_line1 = "123 Main St",
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private static async Task<CatalogItemDto> CreateUnitAsync(HttpClient client, Guid buildingId, string unitNo)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new { fields = new { kind = "Unit", parent_property_id = buildingId, unit_no = unitNo } });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await resp.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
        created.Should().NotBeNull();
        return created!;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
