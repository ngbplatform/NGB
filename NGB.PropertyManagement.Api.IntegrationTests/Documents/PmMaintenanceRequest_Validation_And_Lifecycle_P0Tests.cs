using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmMaintenanceRequest_Validation_And_Lifecycle_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmMaintenanceRequest_Validation_And_Lifecycle_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_WhenCategoryWasMarkedForDeletion_Returns400_WithFriendlyFieldError()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var (party, property, category) = await CreatePrerequisitesAsync(catalogs);

            await catalogs.MarkForDeletionAsync(PropertyManagementCodes.MaintenanceCategory, category.Id, CancellationToken.None);

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.MaintenanceRequest}",
                new
                {
                    fields = new
                    {
                        property_id = property.Id,
                        party_id = party.Id,
                        category_id = category.Id,
                        priority = "normal",
                        subject = "Kitchen sink leak",
                        description = "Water under the sink",
                        requested_at_utc = "2026-03-10"
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.maintenance_request.category.deleted");
            root.GetProperty("detail").GetString().Should().Be("Selected maintenance category is marked for deletion.");
            root.GetProperty("error").GetProperty("errors").GetProperty("category_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected maintenance category is marked for deletion.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Post_WhenCategoryWasMarkedForDeletionAfterDraft_Returns400_WithFriendlyFieldError()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var (party, property, category) = await CreatePrerequisitesAsync(catalogs);

            var request = await documents.CreateDraftAsync(
                PropertyManagementCodes.MaintenanceRequest,
                Payload(new
                {
                    property_id = property.Id,
                    party_id = party.Id,
                    category_id = category.Id,
                    priority = "normal",
                    subject = "Kitchen sink leak",
                    description = "Water under the sink",
                    requested_at_utc = "2026-03-10"
                }),
                CancellationToken.None);

            await catalogs.MarkForDeletionAsync(PropertyManagementCodes.MaintenanceCategory, category.Id, CancellationToken.None);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.MaintenanceRequest}/{request.Id}/post", content: null);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.maintenance_request.category.deleted");
            root.GetProperty("detail").GetString().Should().Be("Selected maintenance category is marked for deletion.");
            root.GetProperty("error").GetProperty("errors").GetProperty("category_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected maintenance category is marked for deletion.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Post_Repost_Unpost_Post_Works_EndToEnd()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var (party, property, category) = await CreatePrerequisitesAsync(catalogs);

            var created = await documents.CreateDraftAsync(
                PropertyManagementCodes.MaintenanceRequest,
                Payload(new
                {
                    property_id = property.Id,
                    party_id = party.Id,
                    category_id = category.Id,
                    priority = "normal",
                    subject = "Kitchen sink leak",
                    description = "Water under the sink",
                    requested_at_utc = "2026-03-10"
                }),
                CancellationToken.None);

            created.Status.Should().Be(DocumentStatus.Draft);
            created.Number.Should().StartWith("MR-");
            created.Payload.Fields!["priority"].GetString().Should().Be("Normal");

            var posted = await documents.PostAsync(PropertyManagementCodes.MaintenanceRequest, created.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);
            posted.Number.Should().Be(created.Number);
            posted.Payload.Fields!["display"].GetString().Should().Contain(posted.Number!);

            var reposted = await documents.RepostAsync(PropertyManagementCodes.MaintenanceRequest, created.Id, CancellationToken.None);
            reposted.Status.Should().Be(DocumentStatus.Posted);
            reposted.Number.Should().Be(created.Number);
            reposted.Payload.Fields!["subject"].GetString().Should().Be("Kitchen sink leak");

            var unposted = await documents.UnpostAsync(PropertyManagementCodes.MaintenanceRequest, created.Id, CancellationToken.None);
            unposted.Status.Should().Be(DocumentStatus.Draft);
            unposted.Number.Should().Be(created.Number);

            var postedAgain = await documents.PostAsync(PropertyManagementCodes.MaintenanceRequest, created.Id, CancellationToken.None);
            postedAgain.Status.Should().Be(DocumentStatus.Posted);
            postedAgain.Number.Should().Be(created.Number);
            postedAgain.Payload.Fields!["subject"].GetString().Should().Be("Kitchen sink leak");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(CatalogItemDto Party, CatalogItemDto Property, CatalogItemDto Category)> CreatePrerequisitesAsync(ICatalogService catalogs)
    {
        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
        {
            display = "John Resident",
            is_tenant = true,
            is_vendor = false
        }), CancellationToken.None);

        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            address_line1 = "101 Main St",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);

        var category = await catalogs.CreateAsync(PropertyManagementCodes.MaintenanceCategory, Payload(new
        {
            display = "Plumbing"
        }), CancellationToken.None);

        return (party, property, category);
    }

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private static async Task DisposeFactoryAsync(PmApiFactory factory)
    {
        try { await factory.DisposeAsync(); }
        catch { /* ignore */ }
    }
}
