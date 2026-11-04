using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class ApiCompositionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Missing_connection_configuration_refuses_startup(string? connectionString)
    {
        using var application = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        });

        var error = Assert.Throws<InvalidOperationException>(() => application.CreateClient());
        Assert.Equal("ConnectionStrings:DefaultConnection is required.", error.Message);
    }

    [Fact]
    public async Task Actual_host_exposes_health_metadata_access_and_audit_through_public_controllers()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var anonymous = host.CreateAnonymousClient();
        using var health = await anonymous.GetAsync("health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var unauthorized = await anonymous.GetAsync("api/catalogs/example.equipment/metadata");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var administrator = await host.CreateClientAsync("administrator");
        var metadata = await administrator.GetFromJsonAsync<JsonElement>("api/documents/example.inspection/metadata");
        Assert.Contains("Inspection result", metadata.ToString());
        Assert.Contains("example.equipment", metadata.ToString());
        var access = await administrator.GetFromJsonAsync<JsonElement>("api/security/me/access");
        Assert.True(access.GetProperty("isBootstrapAdmin").GetBoolean());
        await host.LinkUserAsync("administrator", true, InspectionHost.AdministratorRoleCode, "Application administrator");
        using var workCenter = await administrator.GetAsync("api/work-center/summary");
        Assert.Equal(HttpStatusCode.OK, workCenter.StatusCode);
        using var audit = await administrator.GetAsync("api/audit/entities/9/01900000-0000-7000-8000-000000000001");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
    }
}
