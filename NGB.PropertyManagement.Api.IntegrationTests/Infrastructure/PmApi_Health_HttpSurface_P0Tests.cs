using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmApi_Health_HttpSurface_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmApi_Health_HttpSurface_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Health_Returns_Healthy_When_Postgres_And_Keycloak_Are_Reachable()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateAnonymousClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = await ReadJsonAsync(response);
        payload.RootElement.GetProperty("status").GetString().Should().Be("Healthy");

        var entries = payload.RootElement.GetProperty("entries");
        entries.GetProperty("Web Application").GetProperty("status").GetString().Should().Be("Healthy");
        entries.GetProperty("PostgreSQL Server").GetProperty("status").GetString().Should().Be("Healthy");
        entries.GetProperty("Keycloak").GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task Health_Returns_Unhealthy_When_Keycloak_Is_Unreachable()
    {
        await using var factory = new PmApiFactory(
            _fixture,
            configurationOverrides: new Dictionary<string, string?>
            {
                ["KeycloakSettings:Issuer"] = "http://127.0.0.1:1/realms/ngb",
                ["KeycloakSettings:RequireHttpsMetadata"] = bool.FalseString
            });

        using var client = factory.CreateAnonymousClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        using var payload = await ReadJsonAsync(response);
        payload.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");

        var entries = payload.RootElement.GetProperty("entries");
        entries.GetProperty("PostgreSQL Server").GetProperty("status").GetString().Should().Be("Healthy");

        var keycloak = entries.GetProperty("Keycloak");
        keycloak.GetProperty("status").GetString().Should().Be("Unhealthy");
        keycloak.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}
