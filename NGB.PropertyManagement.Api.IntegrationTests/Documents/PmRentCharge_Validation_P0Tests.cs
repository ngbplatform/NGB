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
using NGB.PropertyManagement.Runtime;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmRentCharge_Validation_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmRentCharge_Validation_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_WhenPeriodFallsOutsideLeaseTerm_Returns400_WithFriendlyFieldErrors()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var (_, _, lease) = await CreateLeaseAsync(catalogs, documents, leaseStartOn: "2026-02-01", leaseEndOn: "2026-02-28");

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.RentCharge}",
                new
                {
                    fields = new
                    {
                        lease_id = lease.Id,
                        period_from_utc = "2026-01-01",
                        period_to_utc = "2026-01-31",
                        due_on_utc = "2026-01-05",
                        amount = 123.45m,
                        memo = "January rent"
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.rent_charge.period.outside_lease_term");
            root.GetProperty("detail").GetString().Should().Be("Rent charge period must stay within lease term (02/01/2026 → 02/28/2026).");

            var errors = root.GetProperty("error").GetProperty("errors");
            errors.GetProperty("period_from_utc").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Rent charge period must stay within lease term (02/01/2026 → 02/28/2026).");
            errors.GetProperty("period_to_utc").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Rent charge period must stay within lease term (02/01/2026 → 02/28/2026).");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Create_WhenAmountIsNotPositive_Returns400_WithFriendlyFieldError()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            var (_, _, lease) = await CreateLeaseAsync(catalogs, documents, leaseStartOn: "2026-02-01", leaseEndOn: "2026-02-28");

            using var resp = await client.PostAsJsonAsync(
                $"/api/documents/{PropertyManagementCodes.RentCharge}",
                new
                {
                    fields = new
                    {
                        lease_id = lease.Id,
                        period_from_utc = "2026-02-01",
                        period_to_utc = "2026-02-28",
                        due_on_utc = "2026-02-05",
                        amount = 0m,
                        memo = "Zero rent"
                    }
                });

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.rent_charge.amount.must_be_positive");
            root.GetProperty("detail").GetString().Should().Be("Amount must be positive.");
            root.GetProperty("error").GetProperty("errors").GetProperty("amount").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Amount must be positive.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    [Fact]
    public async Task Post_WhenLeaseWasMarkedForDeletionAfterDraft_Returns400_WithFriendlyFieldError()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var (_, _, lease) = await CreateLeaseAsync(catalogs, documents, leaseStartOn: "2026-02-01", leaseEndOn: "2026-02-28");
            var rentCharge = await documents.CreateDraftAsync(
                PropertyManagementCodes.RentCharge,
                Payload(new
                {
                    lease_id = lease.Id,
                    period_from_utc = "2026-02-01",
                    period_to_utc = "2026-02-28",
                    due_on_utc = "2026-02-05",
                    amount = "123.45",
                    memo = "Rent"
                }),
                CancellationToken.None);

            var marked = await documents.MarkForDeletionAsync(PropertyManagementCodes.Lease, lease.Id, CancellationToken.None);
            marked.Status.Should().Be(DocumentStatus.MarkedForDeletion);

            using var resp = await client.PostAsync($"/api/documents/{PropertyManagementCodes.RentCharge}/{rentCharge.Id}/post", content: null);

            resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.rent_charge.lease.deleted");
            root.GetProperty("detail").GetString().Should().Be("Selected lease is marked for deletion.");
            root.GetProperty("error").GetProperty("errors").GetProperty("lease_id").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("Selected lease is marked for deletion.");
        }
        finally
        {
            await DisposeFactoryAsync(factory);
        }
    }

    private static async Task<(CatalogItemDto Party, CatalogItemDto Unit, DocumentDto Lease)> CreateLeaseAsync(
        ICatalogService catalogs,
        IDocumentService documents,
        string leaseStartOn,
        string leaseEndOn)
    {
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

        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit.Id,
            start_on_utc = leaseStartOn,
            end_on_utc = leaseEndOn,
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

        return (party, unit, lease);
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
