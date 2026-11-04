using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.PostgreSql.Bootstrap;
using Testcontainers.PostgreSql;
using Xunit;

namespace NgbApplication.IntegrationTests;

public sealed class ApplicationHostTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Api_without_a_connection_string_refuses_startup(string? connectionString)
    {
        using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        });

        var error = Assert.Throws<InvalidOperationException>(() => application.CreateClient());
        Assert.Equal("ConnectionStrings:DefaultConnection is required.", error.Message);
    }

    [Fact]
    public async Task Actual_api_host_uses_disposable_storage_and_enforces_administrator_access()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await DatabaseBootstrapper.InitializeAsync(postgres.GetConnectionString(), default);
        await DatabaseBootstrapper.InitializeAsync(postgres.GetConnectionString(), default);

        await using var identity = new TestIdentity();
        await identity.StartAsync();
        await using var application = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", postgres.GetConnectionString());
            builder.UseSetting("KeycloakSettings:Issuer", identity.Issuer);
            builder.UseSetting("KeycloakSettings:RequireHttpsMetadata", "false");
            builder.UseSetting("KeycloakSettings:ClientIds:0", TestIdentity.ClientId);
            builder.UseSetting("FeatureManagement:Mcp", "false");
            builder.UseSetting("FeatureManagement:AiAssistant", "false");
            builder.UseSetting("FeatureManagement:Attachments", "false");
            builder.UseSetting("FeatureManagement:Notes", "false");
        });

        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var anonymous = await client.GetAsync("/api/security/roles");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await identity.TokenAsync("administrator"));
        using var administrator = await client.GetAsync("/api/security/roles");
        Assert.Equal(HttpStatusCode.OK, administrator.StatusCode);

        foreach (var path in new[]
        {
            "/api/main-menu",
            "/api/catalogs/metadata",
            "/api/audit/entities/SecurityRole/019a0c12-8000-7000-8000-000000000001"
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Personal Work Center state needs a linked platform user, even when
        // the trusted administrator can manage roles before that record exists.
        using var personalWorkCenter = await client.GetAsync("/api/work-center/summary");
        Assert.Equal(HttpStatusCode.Forbidden, personalWorkCenter.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await identity.TokenAsync("reader"));
        using var forbidden = await client.GetAsync("/api/security/roles");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
