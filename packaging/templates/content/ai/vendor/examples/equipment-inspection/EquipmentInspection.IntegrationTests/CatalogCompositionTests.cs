using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Catalogs;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Definitions.Validation;
using NGB.Runtime.UnitOfWork;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class CatalogCompositionTests
{
    private const string Path = "/api/catalogs/example.equipment";

    [Fact]
    public async Task Extended_catalog_validates_real_upserts_and_preserves_storage_and_audit_on_rejection()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var definition = host.Services.GetRequiredService<DefinitionsRegistry>().GetCatalog(EquipmentDefinitions.EquipmentCode);
        Assert.Equal(EquipmentDefinitions.EquipmentCode, definition.Metadata.CatalogCode);
        Assert.Equal(typeof(EquipmentNameValidator), Assert.Single(definition.ValidatorTypes));
        var beforePage = await admin.GetStringAsync(Path);
        using var invalidCreate = await admin.PostAsJsonAsync(Path, Payload(("display", "Bench\nA")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);
        Assert.Contains("single-line", await invalidCreate.Content.ReadAsStringAsync());
        Assert.Equal(beforePage, await admin.GetStringAsync(Path));

        var saved = await PostAsync<CatalogItemDto>(admin, Path, Payload(("display", "Bench A")));
        var record = await admin.GetStringAsync($"{Path}/{saved.Id}");
        var audit = await admin.GetStringAsync($"/api/audit/entities/Catalog/{saved.Id}");
        using var invalidUpdate = await admin.PutAsJsonAsync($"{Path}/{saved.Id}", Payload(("display", "Bench\tB")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdate.StatusCode);
        Assert.Equal(record, await admin.GetStringAsync($"{Path}/{saved.Id}"));
        Assert.Equal(audit, await admin.GetStringAsync($"/api/audit/entities/Catalog/{saved.Id}"));
        using var unchanged = await admin.PutAsJsonAsync($"{Path}/{saved.Id}", Payload());
        await EnsureSuccessAsync(unchanged);
        Assert.Equal("Bench A", (await unchanged.Content.ReadFromJsonAsync<CatalogItemDto>(Json))!.Display);
        using var validUpdate = await admin.PutAsJsonAsync($"{Path}/{saved.Id}", Payload(("display", "Bench B")));
        await EnsureSuccessAsync(validUpdate);
        Assert.Equal("Bench B", (await admin.GetFromJsonAsync<CatalogItemDto>($"{Path}/{saved.Id}", Json))!.Display);
    }

    [Fact]
    public async Task Lookup_is_bounded_filters_deleted_records_and_requires_type_access()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var saved = new List<CatalogItemDto>();
        foreach (var name in new[] { "Lookup Alpha", "Lookup Bravo", "Lookup Charlie" })
            saved.Add(await PostAsync<CatalogItemDto>(admin, Path, Payload(("display", name))));
        await PostAsync<CatalogItemDto>(admin, Path, Payload(("display", "Unrelated")));
        var first = await admin.GetFromJsonAsync<LookupItemDto[]>(Path + "/lookup?q=Lookup&limit=2", Json);
        Assert.Equal(2, first!.Length);
        Assert.All(first, item => Assert.StartsWith("Lookup", item.Label));
        using var deleted = await admin.PostAsJsonAsync($"{Path}/{saved[0].Id}/mark-for-deletion", new { });
        await EnsureSuccessAsync(deleted);
        var remaining = await admin.GetFromJsonAsync<LookupItemDto[]>(Path + "/lookup?q=Lookup&limit=10", Json);
        Assert.Equal(saved.Skip(1).Select(item => item.Id).Order(), remaining!.Select(item => item.Id).Order());
        Assert.Empty((await admin.GetFromJsonAsync<LookupItemDto[]>(Path + "/lookup?q=Lookup&limit=0", Json))!);
        using var reader = await host.CreateClientAsync("reader");
        using var denied = await reader.GetAsync(Path + "/lookup?q=Lookup&limit=2");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain("Lookup Alpha", await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Typed_draft_storage_joins_the_outer_transaction_and_rolls_back_with_its_header_and_audit()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var storage = scope.ServiceProvider.GetRequiredService<ICatalogTypeStorageResolver>()
            .TryResolve(EquipmentDefinitions.EquipmentCode);
        Assert.IsType<PostgresHeadCatalogTypeStorage>(storage);
        var committed = await drafts.CreateAsync(EquipmentDefinitions.EquipmentCode);
        var existing = await admin.GetFromJsonAsync<CatalogItemDto>($"{Path}/{committed}", Json);
        Assert.Equal("DRAFT-" + committed.ToString("N"), existing!.Display);

        var rolledBack = Guid.Empty;
        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteInUowTransactionAsync(async ct =>
        {
            rolledBack = await drafts.CreateAsync(EquipmentDefinitions.EquipmentCode, manageTransaction: false, ct: ct);
            Assert.True(uow.HasActiveTransaction);
            await storage!.EnsureCreatedAsync(rolledBack, ct);
            using var uncommitted = await admin.GetAsync($"{Path}/{rolledBack}");
            Assert.Equal(HttpStatusCode.NotFound, uncommitted.StatusCode);
            throw new InvalidOperationException("Injected failure after typed storage creation.");
        }));
        Assert.NotEqual(Guid.Empty, rolledBack);
        Assert.False(uow.HasActiveTransaction);
        using var absent = await admin.GetAsync($"{Path}/{rolledBack}");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        var audit = await admin.GetFromJsonAsync<JsonElement>($"/api/audit/entities/Catalog/{rolledBack}");
        Assert.Empty(audit.GetProperty("items").EnumerateArray());
        Assert.Equal(existing.Display, (await admin.GetFromJsonAsync<CatalogItemDto>($"{Path}/{committed}", Json))!.Display);
    }

    [Fact]
    public async Task Declared_validator_without_its_runtime_registration_prevents_host_startup()
    {
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.RemoveAll<EquipmentNameValidator>();
            services.RemoveAll<ICatalogUpsertValidator>();
        });
        var error = await Assert.ThrowsAsync<DefinitionsValidationException>(host.StartAsync);
        Assert.Contains(error.Errors, message => message.Contains(nameof(EquipmentNameValidator), StringComparison.Ordinal));
    }
}
