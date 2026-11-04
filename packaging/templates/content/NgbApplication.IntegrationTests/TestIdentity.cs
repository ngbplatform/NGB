using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace NgbApplication.IntegrationTests;

internal sealed class TestIdentity : IAsyncDisposable
{
    private readonly string _realm = "ngb-kit-" + Guid.NewGuid().ToString("N");
    private readonly string _password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly IContainer _container;

    public const string ClientId = "ngb-test-client";

    public string Issuer => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}/realms/{_realm}";

    public TestIdentity()
    {
        var realm = new
        {
            realm = _realm,
            enabled = true,
            sslRequired = "none",
            roles = new { realm = new[] { new { name = "ngb-admin" } } },
            clients = new[]
            {
                new
                {
                    clientId = ClientId,
                    publicClient = true,
                    standardFlowEnabled = true,
                    directAccessGrantsEnabled = true,
                    redirectUris = new[] { "http://127.0.0.1/*", "https://127.0.0.1/*" },
                    webOrigins = new[] { "+" }
                }
            },
            users = new[]
            {
                User("administrator", ["ngb-admin"]),
                User("reader", [])
            }
        };

        _container = new ContainerBuilder("quay.io/keycloak/keycloak:26.5.6")
            .WithPortBinding(8080, true)
            .WithCommand("start-dev", "--hostname-strict=false", "--import-realm")
            .WithResourceMapping(JsonSerializer.SerializeToUtf8Bytes(realm), $"/opt/keycloak/data/import/{_realm}.json")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(8080).ForPath($"/realms/{_realm}/.well-known/openid-configuration")))
            .Build();
    }

    public Task StartAsync() => _container.StartAsync();

    public async Task<string> TokenAsync(string username)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = _password
        });
        using var response = await client.PostAsync($"{Issuer}/protocol/openid-connect/token", body);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<JsonElement>();
        return token.GetProperty("access_token").GetString()!;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private object User(string username, string[] roles) => new
    {
        username,
        firstName = "Test",
        lastName = username,
        email = username + "@example.test",
        enabled = true,
        emailVerified = true,
        realmRoles = roles,
        credentials = new[] { new { type = "password", value = _password, temporary = false } }
    };
}
