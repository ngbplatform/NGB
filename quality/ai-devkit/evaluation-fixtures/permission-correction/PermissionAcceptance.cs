using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class PermissionAcceptance
{
    internal const string AdministratorCode = "equipment.administrator";
    internal const string EquipmentPath = "/api/catalogs/example.equipment";

    [Fact]
    public async Task Active_application_administrator_receives_new_capabilities_and_equipment_edit_without_manual_grants()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("application-administrator", true, AdministratorCode, "Renamed equipment supervisors");
        using var admin = await host.CreateClientAsync("application-administrator");
        await AssertAdministratorAsync(admin);
    }

    [Fact]
    public async Task Trusted_keycloak_administrator_without_a_platform_record_can_edit_equipment()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        await AssertAdministratorAsync(admin);
    }

    [Fact]
    public async Task View_only_reader_receives_read_only_metadata_and_cannot_mutate_equipment()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await AddReaderAsync(host);
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, EquipmentPath, Payload(("display", "Protected equipment")));
        var path = EquipmentPath + "/" + equipment.Id;
        var before = await admin.GetStringAsync(path);
        using var reader = await host.CreateClientAsync("reader");
        var saved = await reader.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.Equal("Protected equipment", saved!.Display);
        var metadata = await reader.GetFromJsonAsync<CatalogTypeMetadataDto>(EquipmentPath + "/metadata", Json);
        Assert.False(metadata!.Capabilities!.CanCreate);
        Assert.False(metadata.Capabilities.CanEdit);
        Assert.False(metadata.Capabilities.CanMarkForDeletion);
        using var create = await reader.PostAsJsonAsync(EquipmentPath, Payload(("display", "Denied")));
        using var update = await reader.PutAsJsonAsync(path, Payload(("display", "Denied")));
        using var deletion = await reader.PostAsJsonAsync(path + "/mark-for-deletion", new { });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deletion.StatusCode);
        Assert.Equal(before, await admin.GetStringAsync(path));
    }

    [Fact]
    public async Task Inactive_and_display_name_only_administrators_remain_blocked()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("inactive-administrator", false, AdministratorCode, "Administrator");
        await host.LinkUserAsync("imitator", true, "equipment.observer", "Administrator");
        foreach (var username in new[] { "inactive-administrator", "imitator" })
        {
            using var denied = await host.CreateClientAsync(username);
            using var permission = await denied.GetAsync("/api/example-access/registered-permission");
            using var create = await denied.PostAsJsonAsync(EquipmentPath, Payload(("display", "Denied")));
            Assert.Equal(HttpStatusCode.Forbidden, permission.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        }
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync(EquipmentPath, Payload(("display", "Anonymous")));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task Administrator_permissions_do_not_enable_a_disabled_deployment_feature()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var disabled = await admin.PostAsync("/api/example-access/disabled-feature", null);
        await AssertErrorAsync(disabled, HttpStatusCode.NotFound, "feature.disabled");
        using var reader = await host.CreateClientAsync("reader");
        using var denied = await reader.PostAsync("/api/example-access/disabled-feature", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    internal static async Task AddReaderAsync(InspectionHost host)
    {
        await host.LinkUserAsync("reader", true, "equipment.reader", "Equipment reader");
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var role = await services.GetRequiredService<IPlatformRoleRepository>().GetByCodeAsync("equipment.reader");
        await services.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(ct =>
            services.GetRequiredService<IPermissionSnapshotRepository>().ReplaceRolePermissionsAsync(role!.RoleId,
                [new NgbPermissionKey(NgbResourceKinds.Catalog, "example.equipment", NgbPermissionActions.View)], ct));
    }

    private static async Task AssertAdministratorAsync(HttpClient admin)
    {
        using var permission = await admin.GetAsync("/api/example-access/registered-permission");
        await EnsureSuccessAsync(permission);
        Assert.True(await permission.Content.ReadFromJsonAsync<bool>());
        var metadata = await admin.GetFromJsonAsync<CatalogTypeMetadataDto>(EquipmentPath + "/metadata", Json);
        Assert.True(metadata!.Capabilities!.CanCreate);
        Assert.True(metadata.Capabilities.CanEdit);
        var created = await PostAsync<CatalogItemDto>(admin, EquipmentPath, Payload(("display", "Before edit")));
        var path = EquipmentPath + "/" + created.Id;
        using var update = await admin.PutAsJsonAsync(path, Payload(("display", "After edit")));
        await EnsureSuccessAsync(update);
        Assert.Equal("After edit", (await admin.GetFromJsonAsync<CatalogItemDto>(path, Json))!.Display);
    }
}
