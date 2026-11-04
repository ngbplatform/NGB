using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

internal sealed class TestIdentity : IAsyncDisposable
{
    private readonly string _realm = "ngb-kit-" + Guid.NewGuid().ToString("N");
    private readonly string _password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly IContainer _container;
    private readonly Dictionary<string, string> _subjects = new(StringComparer.Ordinal);

    public const string ClientId = "ngb-test-client";

    public string Password => _password;

    public string Issuer => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}/realms/{_realm}";

    public TestIdentity(string? browserOrigin = null)
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
                    redirectUris = new[] { browserOrigin is null ? "http://127.0.0.1/*" : browserOrigin + "/*", "https://localhost/signin-oidc" },
                    webOrigins = new[] { "+" }
                }
            },
            users = new[]
            {
                User("administrator", ["ngb-admin"]),
                User("application-administrator", []),
                User("imitator", []),
                User("inactive-administrator", ["ngb-admin"]),
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

    public string Subject(string username) => _subjects[username];

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

    public async Task<string> AuthorizationCodeTokenAsync(string username)
    {
        const string redirect = "http://127.0.0.1/mcp-callback";
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var authorization = QueryHelpers.AddQueryString($"{Issuer}/protocol/openid-connect/auth", new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["scope"] = "openid",
            ["redirect_uri"] = redirect,
            ["state"] = state,
            ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256"
        });
        using var login = await client.GetAsync(authorization);
        login.EnsureSuccessStatusCode();
        // Browsers send Secure cookies to trustworthy loopback HTTP origins. HttpClient
        // does not; apply that exception only inside this disposable local fixture.
        Assert.True(new Uri(Issuer).IsLoopback);
        foreach (Cookie cookie in handler.CookieContainer.GetAllCookies())
            cookie.Secure = false;

        var html = await login.Content.ReadAsStringAsync();
        var form = Regex.Match(html, "<form\\b[^>]*\\bid=\"kc-form-login\"[^>]*\\baction=\"([^\"]+)\"",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        Assert.True(form.Success, "The pinned identity provider must expose its login form.");
        var action = new Uri(WebUtility.HtmlDecode(form.Groups[1].Value));
        Assert.Equal(new Uri(Issuer).Authority, action.Authority);
        using var credentials = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = _password,
            ["credentialId"] = ""
        });
        using var authenticated = await client.PostAsync(action, credentials);
        if (authenticated.StatusCode != HttpStatusCode.Redirect)
        {
            var rejected = await authenticated.Content.ReadAsStringAsync();
            var message = Regex.Match(rejected, "<div id=\"kc-error-message\">(.*?)</div>",
                RegexOptions.Singleline, TimeSpan.FromSeconds(1)).Groups[1].Value;
            Assert.Fail("Identity login failed: " + Regex.Replace(message, "<[^>]+>", "", RegexOptions.None, TimeSpan.FromSeconds(1)));
        }
        var callback = authenticated.Headers.Location!;
        Assert.Equal(redirect, callback.GetLeftPart(UriPartial.Path));
        var parameters = QueryHelpers.ParseQuery(callback.Query);
        Assert.Equal(state, parameters["state"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(parameters["code"]));
        using var exchange = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirect,
            ["code"] = parameters["code"].ToString(),
            ["code_verifier"] = verifier
        });
        using var response = await client.PostAsync($"{Issuer}/protocol/openid-connect/token", exchange);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<JsonElement>();
        return token.GetProperty("access_token").GetString()!;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    private object User(string username, string[] roles)
    {
        var subject = Guid.NewGuid().ToString();
        _subjects.Add(username, subject);
        return new
        {
            id = subject,
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
}
