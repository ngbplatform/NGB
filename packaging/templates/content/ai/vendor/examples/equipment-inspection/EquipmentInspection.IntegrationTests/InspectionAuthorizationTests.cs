using System.Net;
using System.Net.Http.Json;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionAuthorizationTests
{
    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Active_administrators_receive_new_and_content_permissions_without_grants(string username)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode,
                "Renamed equipment supervisors");
        }

        using var admin = await host.CreateClientAsync(username);
        using var permission = await admin.GetAsync("/api/example-access/registered-permission");
        await EnsureSuccessAsync(permission);
        Assert.True(await permission.Content.ReadFromJsonAsync<bool>());
        var content = await admin.GetFromJsonAsync<bool[]>("/api/example-access/content-permissions");
        Assert.Equal(7, content!.Length);
        Assert.All(content, allowed => Assert.True(allowed));
        var (path, version) = await CreateCompleteInspectionAsync(admin);
        using var posted = await ExecutePostAsync(admin, path, version, "administrator-post");
        await EnsureSuccessAsync(posted);

        using var disabled = await admin.PostAsync("/api/example-access/disabled-feature", null);
        await AssertErrorAsync(disabled, HttpStatusCode.NotFound, "feature.disabled");
    }

    [Theory]
    [InlineData("reader")]
    [InlineData("imitator")]
    [InlineData("inactive-administrator")]
    public async Task Limited_inactive_and_display_name_only_actors_are_denied(string username)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (username == "imitator")
            await host.LinkUserAsync(username, true, "equipment.observer", "Administrator");
        if (username == "inactive-administrator")
            await host.LinkUserAsync(username, false, InspectionHost.AdministratorRoleCode, "Administrator");

        using var client = await host.CreateClientAsync(username);
        using var permission = await client.GetAsync("/api/example-access/registered-permission");
        Assert.Equal(HttpStatusCode.Forbidden, permission.StatusCode);
        var content = await client.GetFromJsonAsync<bool[]>("/api/example-access/content-permissions");
        Assert.Equal(7, content!.Length);
        Assert.All(content, allowed => Assert.False(allowed));
        using var create = await client.PostAsJsonAsync("/api/documents/example.inspection", Payload());
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using var disabled = await client.PostAsync("/api/example-access/disabled-feature", null);
        Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode);

        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.GetAsync("/api/example-access/registered-permission");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }
}
