using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class DocumentService_PmLease_Scaffolds_And_Posting_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public DocumentService_PmLease_Scaffolds_And_Posting_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostAsync_Posts_And_Unpost_RestoresDraft()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
                        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
                    {
                        kind = "Building",
                        address_line1 = "A",
                        city = "Hoboken",
                        state = "NJ",
                        zip = "07030"
                    }), CancellationToken.None);
            
                        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
                    {
                        kind = "Unit",
                        parent_property_id = building.Id,
                        unit_no = "1A"
                    }), CancellationToken.None);


            var created = await documents.CreateDraftAsync(
                PropertyManagementCodes.Lease,
                Payload(
                    new
                    {
                        property_id = unit.Id,
                        start_on_utc = "2026-02-01",
                        rent_amount = "1000.00"
                    },
                    LeaseParts.PrimaryTenant(party.Id)),
                CancellationToken.None);

            var posted = await documents.PostAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
            posted.Status.Should().Be(DocumentStatus.Posted);

            var reloaded = await documents.GetByIdAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
            reloaded.Status.Should().Be(DocumentStatus.Posted);

            var unposted = await documents.UnpostAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
            unposted.Status.Should().Be(DocumentStatus.Draft);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostAsync_WhenPropertyIsBuilding_ThrowsValidation()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);

	            // Building property must not be allowed for a lease.
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
            {
                kind = "Building",
                address_line1 = "A",
                city = "Hoboken",
                state = "NJ",
                zip = "07030"
            }), CancellationToken.None);

	            var act = () => documents.CreateDraftAsync(
	                PropertyManagementCodes.Lease,
	                Payload(
	                    new
	                    {
	                        property_id = building.Id,
	                        start_on_utc = "2026-02-01",
	                        rent_amount = "1000.00"
	                    },
	                    LeaseParts.PrimaryTenant(party.Id)),
	                CancellationToken.None);
            var ex = await act.Should().ThrowAsync<NgbValidationException>();
            ex.Which.ErrorCode.Should().Be("pm.lease.property.must_be_unit");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }


    [Fact]
    public async Task RepostAsync_WhenDraft_ThrowsStateMismatch()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
            address_line1 = "A",
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

            var created = await documents.CreateDraftAsync(
                PropertyManagementCodes.Lease,
                Payload(
                    new
                    {
                        property_id = property.Id,
                        start_on_utc = "2026-02-01",
                        rent_amount = "1000.00"
                    },
                    LeaseParts.PrimaryTenant(party.Id)),
                CancellationToken.None);

            var act = () => documents.RepostAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
            await act.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task UnmarkForDeletion_AllowsEditingAgain()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
            address_line1 = "A",
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

            var created = await documents.CreateDraftAsync(
                PropertyManagementCodes.Lease,
                Payload(
                    new
                    {
                        property_id = property.Id,
                        start_on_utc = "2026-02-01",
                        rent_amount = "1000.00",
                        memo = "old"
                    },
                    LeaseParts.PrimaryTenant(party.Id)),
                CancellationToken.None);

            var marked = await documents.MarkForDeletionAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
            marked.Status.Should().Be(DocumentStatus.MarkedForDeletion);

            var unmarked = await documents.UnmarkForDeletionAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
            unmarked.Status.Should().Be(DocumentStatus.Draft);

            var updated = await documents.UpdateDraftAsync(PropertyManagementCodes.Lease, created.Id, Payload(new { memo = "new" }), CancellationToken.None);
            updated.Payload.Fields!["memo"].GetString().Should().Be("new");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetRelationshipGraphAsync_IsV0Scaffold_ReturnsSingleNodeGraph()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
            address_line1 = "A",
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

            var created = await documents.CreateDraftAsync(
                PropertyManagementCodes.Lease,
                Payload(
                    new
                    {
                        property_id = property.Id,
                        start_on_utc = "2026-02-01",
                        rent_amount = "1000.00"
                    },
                    LeaseParts.PrimaryTenant(party.Id)),
                CancellationToken.None);

            var graph = await documents.GetRelationshipGraphAsync(
                documentType: PropertyManagementCodes.Lease,
                id: created.Id,
                depth: 3,
                maxNodes: 100,
                ct: CancellationToken.None);

            graph.Nodes.Should().HaveCount(1);
            graph.Edges.Should().BeEmpty();

            var node = graph.Nodes.Single();
            node.Kind.Should().Be(EntityKind.Document);
            node.TypeCode.Should().Be(PropertyManagementCodes.Lease);
            node.EntityId.Should().Be(created.Id);
            node.NodeId.Should().Be($"doc:{PropertyManagementCodes.Lease}:{created.Id}");
	            node.Title.Should().Be("A #101 — 02/01/2026 → Open");
            node.DocumentStatus.Should().Be(DocumentStatus.Draft);
            node.Depth.Should().Be(0);
            node.Amount.Should().Be(1000.00m);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task GetEffectsAsync_ReturnsUiActionAvailability_AndKeepsEffectsScaffolded()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
            address_line1 = "A",
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

            var created = await documents.CreateDraftAsync(
                PropertyManagementCodes.Lease,
                Payload(
                    new
                    {
                        property_id = property.Id,
                        start_on_utc = "2026-02-01",
                        rent_amount = "1000.00"
                    },
                    LeaseParts.PrimaryTenant(party.Id)),
                CancellationToken.None);

            var effects = await documents.GetEffectsAsync(PropertyManagementCodes.Lease, created.Id, limit: 100, CancellationToken.None);
            effects.AccountingEntries.Should().BeEmpty();
            effects.OperationalRegisterMovements.Should().BeEmpty();
            effects.ReferenceRegisterWrites.Should().BeEmpty();
            effects.Ui.Should().NotBeNull();
            effects.Ui!.IsPosted.Should().BeFalse();
            effects.Ui.CanEdit.Should().BeTrue();
            effects.Ui.CanPost.Should().BeTrue();
            effects.Ui.CanUnpost.Should().BeFalse();
            effects.Ui.CanRepost.Should().BeFalse();
            effects.Ui.CanApply.Should().BeFalse();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task PostAsync_WhenOverlapsAnotherPostedLeaseForSameProperty_ThrowsConflict()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var party1 = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P1" }), CancellationToken.None);
            var party2 = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P2" }), CancellationToken.None);
                        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
                    {
                        kind = "Building",
                        address_line1 = "A",
                        city = "Hoboken",
                        state = "NJ",
                        zip = "07030"
                    }), CancellationToken.None);
            
                        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
                    {
                        kind = "Unit",
                        parent_property_id = building.Id,
                        unit_no = "1A"
                    }), CancellationToken.None);


            var lease1 = await documents.CreateDraftAsync(
                PropertyManagementCodes.Lease,
                Payload(
                    new
                    {
                        property_id = unit.Id,
                        start_on_utc = "2026-02-01",
                        end_on_utc = "2026-02-28",
                        rent_amount = "1000.00"
                    },
                    LeaseParts.PrimaryTenant(party1.Id)),
                CancellationToken.None);

            var lease2 = await documents.CreateDraftAsync(
                PropertyManagementCodes.Lease,
                Payload(
                    new
                    {
                        property_id = unit.Id,
                        start_on_utc = "2026-02-15",
                        end_on_utc = "2026-03-15",
                        rent_amount = "1100.00"
                    },
                    LeaseParts.PrimaryTenant(party2.Id)),
                CancellationToken.None);

            (await documents.PostAsync(PropertyManagementCodes.Lease, lease1.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            var act = () => documents.PostAsync(PropertyManagementCodes.Lease, lease2.Id, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<NgbConflictException>();
            ex.Which.ErrorCode.Should().Be("pm.lease.overlap");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task DeriveAsync_IsV0Scaffold_CreatesNewDraft_UsingInitialPayload()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
            var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
            address_line1 = "A",
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

            var source = await documents.CreateDraftAsync(
                PropertyManagementCodes.Lease,
                Payload(
                    new
                    {
                        property_id = property.Id,
                        start_on_utc = "2026-02-01",
                        rent_amount = "1000.00"
                    },
                    LeaseParts.PrimaryTenant(party.Id)),
                CancellationToken.None);

            var derived = await documents.DeriveAsync(
                targetDocumentType: PropertyManagementCodes.Lease,
                sourceDocumentId: source.Id,
                relationshipType: "based_on",
                initialPayload: Payload(
                    new
                    {
                        property_id = property.Id,
                        start_on_utc = "2026-03-01",
                        rent_amount = "1100.00",
                        memo = "derived"
                    },
                    LeaseParts.PrimaryTenant(party.Id)),
                ct: CancellationToken.None);

            derived.Id.Should().NotBe(source.Id);
            derived.Status.Should().Be(DocumentStatus.Draft);
	            derived.Payload.Fields!["display"].GetString().Should().Be("A #101 — 03/01/2026 → Open");
            derived.Payload.Fields!["start_on_utc"].GetString().Should().Be("2026-03-01");
            derived.Payload.Fields!["rent_amount"].GetDecimal().Should().Be(1100.00m);
            derived.Payload.Fields!["memo"].GetString().Should().Be("derived");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async ValueTask DisposeFactoryAsync(PmApiFactory factory)
    {
        if (factory is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            factory.Dispose();
        }
    }

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in el.EnumerateObject())
        {
            dict[p.Name] = p.Value;
        }

        return new RecordPayload(dict, parts);
    }
}
