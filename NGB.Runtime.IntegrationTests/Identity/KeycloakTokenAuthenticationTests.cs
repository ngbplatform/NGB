using System.Net;
using System.Net.Http.Headers;
using ExternalMcp.Tests;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Testing.Containers;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Identity;

public sealed class KeycloakTokenAuthenticationTests(KeycloakTokenAuthenticationTests.KeycloakApiFixture fixture)
    : IClassFixture<KeycloakTokenAuthenticationTests.KeycloakApiFixture>
{
    [Theory]
    [InlineData("alex.carter", TokenKind.Access, HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData("alex.carter", TokenKind.Id, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData("alex.carter", TokenKind.Refresh, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData("reader", TokenKind.Access, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData("reader", TokenKind.Id, HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    [InlineData("reader", TokenKind.Refresh, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized)]
    public async Task RealKeycloakTokens_RespectCurrentAuthenticationAndAdminPolicy(
        string username,
        TokenKind tokenKind,
        HttpStatusCode expectedAuthentication,
        HttpStatusCode expectedAdminAccess)
    {
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.GetToken(username, tokenKind));

        using var authenticated = await client.GetAsync("/protected");
        using var admin = await client.GetAsync("/admin");

        // ID-token acceptance is part of the current behavior; authentication does not grant an admin role.
        authenticated.StatusCode.Should().Be(expectedAuthentication);
        admin.StatusCode.Should().Be(expectedAdminAccess);
        if (expectedAuthentication == HttpStatusCode.OK)
            (await authenticated.Content.ReadAsStringAsync()).Should().Be(username);
    }

    [Theory]
    [InlineData("/protected")]
    [InlineData("/admin")]
    public async Task AnonymousRequests_AreRejected(string path)
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(TokenKind.Access)]
    [InlineData(TokenKind.Id)]
    public async Task TokensWithModifiedSignatures_AreRejected(TokenKind tokenKind)
    {
        var parts = fixture.GetToken("alex.carter", tokenKind).Split('.');
        var signature = Base64UrlEncoder.DecodeBytes(parts[2]);
        signature[0] ^= 1;
        parts[2] = Base64UrlEncoder.Encode(signature);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', parts));

        using var authenticated = await client.GetAsync("/protected");
        using var admin = await client.GetAsync("/admin");

        authenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        admin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public enum TokenKind
    {
        Access,
        Id,
        Refresh
    }

    public sealed class KeycloakApiFixture : IAsyncLifetime
    {
        private readonly KeycloakFixture _keycloak = new();
        private readonly Dictionary<string, (string AccessToken, string IdToken, string RefreshToken, string Subject)> _tokens = new(StringComparer.Ordinal);
        private IHost? _host;

        public async Task InitializeAsync()
        {
            await using (var startupLease = await TestcontainerStartupGate.AcquireAsync())
                await _keycloak.StartAsync();

            foreach (var username in new[] { "alex.carter", "reader" })
                _tokens.Add(username, await _keycloak.AuthenticateUserWithTokensAsync(username));

            _host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        var configuration = new ConfigurationBuilder()
                            .AddInMemoryCollection(new Dictionary<string, string?>
                            {
                                ["KeycloakSettings:Issuer"] = _keycloak.Issuer,
                                ["KeycloakSettings:RequireHttpsMetadata"] = "false",
                                ["KeycloakSettings:ClientIds:0"] = KeycloakFixture.ClientId
                            })
                            .Build();

                        services.AddRouting();
                        services.AddKeycloak(configuration);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/protected", context =>
                                    context.Response.WriteAsync(context.User.Identity!.Name!))
                                .RequireAuthorization();
                            endpoints.MapGet("/admin", context => context.Response.WriteAsync("allowed"))
                                .RequireAuthorization("AuthAdminPolicy");
                        });
                    }))
                .StartAsync();
        }

        public HttpClient CreateClient() => _host!.GetTestClient();

        public string GetToken(string username, TokenKind tokenKind)
        {
            var tokens = _tokens[username];
            return tokenKind switch
            {
                TokenKind.Access => tokens.AccessToken,
                TokenKind.Id => tokens.IdToken,
                TokenKind.Refresh => tokens.RefreshToken,
                _ => throw new ArgumentOutOfRangeException(nameof(tokenKind), tokenKind, null)
            };
        }

        public async Task DisposeAsync()
        {
            _host?.Dispose();
            await _keycloak.DisposeAsync();
        }
    }
}
