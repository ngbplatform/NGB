using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using NGB.Contracts.Admin;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmApi_KeycloakAuthentication_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmApi_KeycloakAuthentication_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Protected_Endpoint_Rejects_Anonymous_Request()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/api/main-menu");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_Endpoint_Accepts_Real_Keycloak_Token_From_Web_Client()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateAuthenticatedClient(clientId: PmKeycloakTestClients.WebClient);

        using var response = await client.GetAsync("/api/main-menu");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<MainMenuDto>();
        payload.Should().NotBeNull();
        payload!.Groups.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Protected_Endpoint_Accepts_Real_Keycloak_Token_From_Tester_Client()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/api/main-menu");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
