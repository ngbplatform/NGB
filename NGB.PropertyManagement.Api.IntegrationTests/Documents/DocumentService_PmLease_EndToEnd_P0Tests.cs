using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Core.Documents.Exceptions;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class DocumentService_PmLease_EndToEnd_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public DocumentService_PmLease_EndToEnd_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_GetById_Search_And_Filter_Work_UsingServiceDirectly()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            // Ensure the host is built.
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            // IMPORTANT: PostgresUnitOfWork is IAsyncDisposable-only. Use async scope disposal.
            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new
        {
            display = "John Smith",
            email = "john.smith@example.com",
            phone = "+1-201-555-0101"
        }), CancellationToken.None);

        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
{
            kind = "Building",
            display = "101 Main St",
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
var created = await documents.CreateDraftAsync(
            PropertyManagementCodes.Lease,
            Payload(
                new
                {
                    property_id = property.Id,
                    start_on_utc = "2026-02-01",
                    end_on_utc = "2027-01-31",
                    rent_amount = "1250.00", // string => invariant parsing
                    due_day = 5,
                    memo = "Initial lease (draft)"
                },
                LeaseParts.PrimaryTenant(party.Id)),
            CancellationToken.None);

        created.Id.Should().NotBe(Guid.Empty);
        created.Status.Should().Be(DocumentStatus.Draft);
        created.Payload.Fields.Should().NotBeNull();

        var loaded = await documents.GetByIdAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
        loaded.Id.Should().Be(created.Id);
        loaded.Payload.Fields!.Should().ContainKey("display");
	    loaded.Payload.Fields!["display"].GetString().Should().Be("101 Main St #101 — 02/01/2026 → 01/31/2027");

        // Search by display (ILIKE)
        var page = await documents.GetPageAsync(PropertyManagementCodes.Lease,
            new PageRequestDto(Offset: 0, Limit: 50, Search: "Main", Filters: null),
            CancellationToken.None);

        page.Items.Should().Contain(i => i.Id == created.Id);

        // Filter by property_id (exact match on text)
        var filtered = await documents.GetPageAsync(PropertyManagementCodes.Lease,
            new PageRequestDto(Offset: 0, Limit: 50, Search: null, Filters: new Dictionary<string, string>
            {
                ["property_id"] = property.Id.ToString("D")
            }),
            CancellationToken.None);

            filtered.Items.Select(i => i.Id).Should().Contain(created.Id);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task UpdateDraft_PartialUpdate_MergesRequiredFields_AndKeepsNotNullInDb()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Alice" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
{
            kind = "Building",
            display = "201 River St",
            address_line1 = "201 River St",
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
                    rent_amount = 999.99m,
                    memo = "old"
                },
                LeaseParts.PrimaryTenant(party.Id)),
            CancellationToken.None);

        // Act: update only optional field
        var updated = await documents.UpdateDraftAsync(PropertyManagementCodes.Lease, created.Id, Payload(new
        {
            memo = "new"
        }), CancellationToken.None);

        updated.Id.Should().Be(created.Id);
        updated.Payload.Fields!.Should().ContainKey("memo");

        // Required fields must be preserved
        var reloaded = await documents.GetByIdAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
        var fields = reloaded.Payload.Fields!;
	    fields["display"].GetString().Should().Be("201 River St #101 — 02/01/2026 → Open");
        fields["property_id"].ParseGuidOrRef().Should().Be(property.Id);
        fields["start_on_utc"].GetString().Should().Be("2026-02-01");
        fields["rent_amount"].GetDecimal().Should().Be(999.99m);
            fields["memo"].GetString().Should().Be("new");

            reloaded.Payload.Parts.Should().NotBeNull();
            reloaded.Payload.Parts!.Should().ContainKey("parties");
            var parties = reloaded.Payload.Parts!["parties"].Rows;
            parties.Should().ContainSingle();
            // Server-side enrichment for references inside payload.parts: party_id => { id, display }
            parties[0]["party_id"].ValueKind.Should().Be(JsonValueKind.Object);
            parties[0]["party_id"].GetProperty("id").GetString().Should().Be(party.Id.ToString("D"));
            parties[0]["party_id"].GetProperty("display").GetString().Should().Be("Alice");
            parties[0]["party_id"].ParseGuidOrRef().Should().Be(party.Id);
            parties[0]["role"].GetString().Should().Be("PrimaryTenant");
            parties[0]["is_primary"].GetBoolean().Should().BeTrue();
            parties[0]["ordinal"].GetInt32().Should().Be(1);
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task UpdateDraft_WhenDisplayIsNull_RecomputesInDb()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Bob" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
{
            kind = "Building",
            display = "301 Hudson",
            address_line1 = "301 Hudson",
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
                    rent_amount = 1200.00m,
                    memo = "old"
                },
                LeaseParts.PrimaryTenant(party.Id)),
            CancellationToken.None);

        // Act: explicitly set display to null and also set end date.
        // DB trigger must compute display regardless of the incoming display value.
        var updated = await documents.UpdateDraftAsync(
            PropertyManagementCodes.Lease,
            created.Id,
            new RecordPayload(Fields: new Dictionary<string, JsonElement>
            {
                ["display"] = JsonSerializer.SerializeToElement<object?>(null),
                ["end_on_utc"] = JsonSerializer.SerializeToElement("2026-02-28")
            }),
            CancellationToken.None);

        updated.Id.Should().Be(created.Id);

        var reloaded = await documents.GetByIdAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
	    reloaded.Payload.Fields!["display"].GetString().Should().Be("301 Hudson #101 — 02/01/2026 → 02/28/2026");
        reloaded.Payload.Fields!["memo"].GetString().Should().Be("old");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task UpdateDraft_WhenMarkedForDeletion_ThrowsDocumentMarkedForDeletionException()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Charlie" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
{
            kind = "Building",
            display = "401 Bloomfield",
            address_line1 = "401 Bloomfield",
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
                    rent_amount = 1500.00m,
                },
                LeaseParts.PrimaryTenant(party.Id)),
            CancellationToken.None);

        // Mark for deletion through the same public service.
        var marked = await documents.MarkForDeletionAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
        marked.Status.Should().Be(DocumentStatus.MarkedForDeletion);

        // Act
        var act = () => documents.UpdateDraftAsync(PropertyManagementCodes.Lease, created.Id, Payload(new { memo = "x" }), CancellationToken.None);

        // Assert
            await act.Should().ThrowAsync<DocumentMarkedForDeletionException>();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task DeleteDraft_RemovesCommonRow_AndTypedHeadRow()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            await using var scope = factory.Services.CreateAsyncScope();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "Donna" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
{
            kind = "Building",
            display = "501 Park",
            address_line1 = "501 Park",
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
                    rent_amount = 2000.00m,
                },
                LeaseParts.PrimaryTenant(party.Id)),
            CancellationToken.None);

        await documents.DeleteDraftAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);

        var act = () => documents.GetByIdAsync(PropertyManagementCodes.Lease, created.Id, CancellationToken.None);
            await act.Should().ThrowAsync<DocumentNotFoundException>();
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async ValueTask DisposeFactoryAsync(PmApiFactory factory)
    {
        // WebApplicationFactory historically implements IDisposable, but newer versions may also implement IAsyncDisposable.
        // Dispose asynchronously when possible, to correctly dispose DI containers with IAsyncDisposable-only services.
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
        // NOTE: Use invariant JSON serialization to preserve types.
        var el = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in el.EnumerateObject())
        {
            dict[p.Name] = p.Value;
        }

        return new RecordPayload(dict, parts);
    }
}
