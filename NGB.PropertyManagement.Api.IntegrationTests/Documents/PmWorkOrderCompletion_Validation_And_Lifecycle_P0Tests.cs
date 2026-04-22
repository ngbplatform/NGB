using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmWorkOrderCompletion_Validation_And_Lifecycle_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmWorkOrderCompletion_Validation_And_Lifecycle_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateDraft_WhenWorkOrderIsStillDraft_Returns400_WithFriendlyFieldError()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var (_, workOrder) = await CreateDraftWorkOrderAsync(catalogs, documents);

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.WorkOrderCompletion}",
                new
                {
                    fields = new
                    {
                        work_order_id = workOrder.Id,
                        closed_at_utc = "2026-03-13",
                        outcome = "completed",
                        resolution_notes = "Done"
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.work_order_completion.work_order.must_be_posted");
            root.GetProperty("detail").GetString().Should().Be("Selected work order must be posted before creating a work order completion.");
            root.GetProperty("error").GetProperty("errors").GetProperty("work_order_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected work order must be posted before creating a work order completion.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Post_WhenAnotherPostedCompletionAlreadyExists_Returns400_WithFriendlyFieldError()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var workOrder = await CreatePostedWorkOrderAsync(catalogs, documents);

            var first = await documents.CreateDraftAsync(
                PropertyManagementCodes.WorkOrderCompletion,
                Payload(new
                {
                    work_order_id = workOrder.Id,
                    closed_at_utc = "2026-03-13",
                    outcome = "completed",
                    resolution_notes = "Resolved"
                }),
                CancellationToken.None);

            var second = await documents.CreateDraftAsync(
                PropertyManagementCodes.WorkOrderCompletion,
                Payload(new
                {
                    work_order_id = workOrder.Id,
                    closed_at_utc = "2026-03-14",
                    outcome = "cancelled",
                    resolution_notes = "Should not post"
                }),
                CancellationToken.None);

            first = await documents.PostAsync(PropertyManagementCodes.WorkOrderCompletion, first.Id, CancellationToken.None);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.WorkOrderCompletion}/{second.Id}/post", content: null);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.work_order_completion.work_order.already_completed");
            root.GetProperty("detail").GetString().Should().Be("Selected work order already has a posted completion.");
            root.GetProperty("error").GetProperty("errors").GetProperty("work_order_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected work order already has a posted completion.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Post_Repost_Unpost_Post_Works_EndToEnd_And_CreatesBasedOnRelationship()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var workOrder = await CreatePostedWorkOrderAsync(catalogs, documents);

            var created = await documents.CreateDraftAsync(
                PropertyManagementCodes.WorkOrderCompletion,
                Payload(new
                {
                    work_order_id = workOrder.Id,
                    closed_at_utc = "2026-03-13",
                    outcome = "unable_to_complete",
                    resolution_notes = "Vendor could not access unit"
                }),
                CancellationToken.None);

            created.Status.Should().Be(DocumentStatus.Draft);
            created.Number.Should().StartWith("WOC-");
            created.Payload.Fields!["outcome"].GetString().Should().Be("UnableToComplete");

            var posted = await documents.PostAsync(PropertyManagementCodes.WorkOrderCompletion, created.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);
            posted.Number.Should().Be(created.Number);
            posted.Payload.Fields!["display"].GetString().Should().Contain(posted.Number!);

            var reposted = await documents.RepostAsync(PropertyManagementCodes.WorkOrderCompletion, created.Id, CancellationToken.None);
            reposted.Status.Should().Be(DocumentStatus.Posted);
            reposted.Number.Should().Be(created.Number);
            reposted.Payload.Fields!["outcome"].GetString().Should().Be("UnableToComplete");

            var unposted = await documents.UnpostAsync(PropertyManagementCodes.WorkOrderCompletion, created.Id, CancellationToken.None);
            unposted.Status.Should().Be(DocumentStatus.Draft);
            unposted.Number.Should().Be(created.Number);

            var postedAgain = await documents.PostAsync(PropertyManagementCodes.WorkOrderCompletion, created.Id, CancellationToken.None);
            postedAgain.Status.Should().Be(DocumentStatus.Posted);
            postedAgain.Number.Should().Be(created.Number);

            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            var relationshipCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                  FROM document_relationships
                 WHERE from_document_id = @fromId
                   AND to_document_id = @toId
                   AND relationship_code_norm = 'based_on';
                """,
                new { fromId = created.Id, toId = workOrder.Id });

            relationshipCount.Should().Be(1);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(DocumentDto Request, DocumentDto WorkOrder)> CreateDraftWorkOrderAsync(ICatalogService catalogs, IDocumentService documents)
    {
        var resident = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "John Resident", is_tenant = true, is_vendor = false }), CancellationToken.None);
        var vendor = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "FixIt Vendor", is_tenant = false, is_vendor = true }), CancellationToken.None);
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
        var category = await catalogs.CreateAsync(PropertyManagementCodes.MaintenanceCategory, Payload(new { display = "Plumbing" }), CancellationToken.None);

        var request = await documents.CreateDraftAsync(
            PropertyManagementCodes.MaintenanceRequest,
            Payload(new
            {
                property_id = property.Id,
                party_id = resident.Id,
                category_id = category.Id,
                priority = "normal",
                subject = "Kitchen sink leak",
                description = "Water under the sink",
                requested_at_utc = "2026-03-10"
            }),
            CancellationToken.None);
        request = await documents.PostAsync(PropertyManagementCodes.MaintenanceRequest, request.Id, CancellationToken.None);

        var workOrder = await documents.CreateDraftAsync(
            PropertyManagementCodes.WorkOrder,
            Payload(new
            {
                request_id = request.Id,
                assigned_party_id = vendor.Id,
                scope_of_work = "Inspect leak and replace trap",
                due_by_utc = "2026-03-12",
                cost_responsibility = "owner"
            }),
            CancellationToken.None);

        return (request, workOrder);
    }

    private static async Task<DocumentDto> CreatePostedWorkOrderAsync(ICatalogService catalogs, IDocumentService documents)
    {
        var (_, workOrder) = await CreateDraftWorkOrderAsync(catalogs, documents);
        return await documents.PostAsync(PropertyManagementCodes.WorkOrder, workOrder.Id, CancellationToken.None);
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
        catch { }
    }
}
