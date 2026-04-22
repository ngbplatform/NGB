using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Catalogs;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmParty_Roles_Validation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmParty_Roles_Validation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_WhenBothRolesFalse_Returns400_WithFriendlyError()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var resp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display = "No Roles",
                    is_tenant = false,
                    is_vendor = false
                }
            });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.party.role_required");
        root.GetProperty("detail").GetString().Should().Be("Select at least one role: Tenant or Vendor.");
        root.GetProperty("error").GetProperty("errors").GetProperty("is_tenant")[0].GetString().Should().Be("Select at least one role: Tenant or Vendor.");
        root.GetProperty("error").GetProperty("errors").GetProperty("is_vendor")[0].GetString().Should().Be("Select at least one role: Tenant or Vendor.");
    }

    [Fact]
    public async Task Update_WhenRolesBecomeFalseFalse_Returns400_WithFriendlyError()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var createResp = await client.PostAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new
            {
                fields = new
                {
                    display = "Tenant Default"
                }
            });

        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<CatalogItemDto>();
        created.Should().NotBeNull();

        var updateResp = await client.PutAsJsonAsync(
            $"/api/catalogs/{PropertyManagementCodes.Party}/{created!.Id}",
            new
            {
                fields = new
                {
                    is_tenant = false,
                    is_vendor = false
                }
            });

        updateResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var doc = JsonDocument.Parse(await updateResp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.party.role_required");
    }
}
