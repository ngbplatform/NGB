using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace ExternalMcp.Tests;

internal sealed class KeycloakFixture : IAsyncDisposable
{
    private const string Realm = "ngb-mcp-contracts";
    internal const string ClientId = "ngb-web-tests";
    private readonly IContainer _container;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _connections = [];
    private Task? _relay;
    internal string Issuer => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/realms/{Realm}";

    internal KeycloakFixture()
    {
        var realm = new
        {
            realm = Realm,
            enabled = true,
            sslRequired = "none",
            roles = new { realm = new[] { new { name = "ngb-admin" } } },
            clients = new[]
            {
                new
                {
                    clientId = ClientId,
                    protocol = "openid-connect",
                    publicClient = true,
                    standardFlowEnabled = true,
                    directAccessGrantsEnabled = false,
                    serviceAccountsEnabled = false,
                    redirectUris = new[] { "http://127.0.0.1:51111/callback" },
                    attributes = new Dictionary<string, string> { ["pkce.code.challenge.method"] = "S256" }
                }
            },
            users = new[]
            {
                new
                {
                    username = "alex.carter",
                    firstName = "Alex",
                    lastName = "Carter",
                    enabled = true,
                    emailVerified = true,
                    email = "alex@example.test",
                    realmRoles = new[] { "ngb-admin" },
                    credentials = new[] { new { type = "password", value = "mcp-test-password", temporary = false } }
                },
                new
                {
                    username = "reader",
                    firstName = "Limited",
                    lastName = "Reader",
                    enabled = true,
                    emailVerified = true,
                    email = "reader@example.test",
                    realmRoles = Array.Empty<string>(),
                    credentials = new[] { new { type = "password", value = "mcp-test-password", temporary = false } }
                }
            }
        };
        _container = new ContainerBuilder("quay.io/keycloak/keycloak:26.5.6")
            .WithPortBinding(8080, true)
            .WithCommand("start-dev", "--hostname-strict=false", "--import-realm")
            .WithResourceMapping(JsonSerializer.SerializeToUtf8Bytes(realm), $"/opt/keycloak/data/import/{Realm}-realm.json")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(8080).ForPath($"/realms/{Realm}/.well-known/openid-configuration")))
            .Build();
    }

    internal async Task StartAsync()
    {
        await _container.StartAsync();
        // Nested Docker runners reach published ports through host.docker.internal.
        // Keep the test issuer on loopback without weakening the server's HTTP policy.
        _listener.Start();
        _relay = RelayAsync();
    }

    private async Task RelayAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                _connections.Add(ForwardAsync(client));
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    private async Task ForwardAsync(TcpClient client)
    {
        using var incoming = client;
        using var outgoing = new TcpClient();
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        try
        {
            await outgoing.ConnectAsync(_container.Hostname, _container.GetMappedPublicPort(8080), stopped.Token);
            var request = incoming.GetStream().CopyToAsync(outgoing.GetStream(), stopped.Token);
            var response = outgoing.GetStream().CopyToAsync(incoming.GetStream(), stopped.Token);
            await Task.WhenAny(request, response);
            await stopped.CancelAsync();
            await Task.WhenAll(request, response);
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            // A closed peer ends this fixture connection; HTTP callers observe failures normally.
        }
    }

    internal async Task<(string AccessToken, string Subject)> AuthenticateUserAsync(string username = "alex.carter")
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));
        const string redirect = "http://127.0.0.1:51111/callback";
        var authorization = $"{Issuer}/protocol/openid-connect/auth?client_id={ClientId}&response_type=code"
            + $"&scope=openid%20profile&redirect_uri={Uri.EscapeDataString(redirect)}&state={state}"
            + $"&code_challenge={challenge}&code_challenge_method=S256";
        using var login = await client.GetAsync(authorization);
        login.EnsureSuccessStatusCode();
        var html = await login.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "<form[^>]*action=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        if (!match.Success)
            throw new InvalidOperationException("Keycloak did not render its user login form.");

        var action = new Uri(WebUtility.HtmlDecode(match.Groups[1].Value));
        if (action.GetLeftPart(UriPartial.Authority) != new Uri(Issuer).GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("Unexpected Keycloak login origin.");

        using var submission = new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = "mcp-test-password"
            })
        };
        // Preserve the issuer's login cookies only for this checked same-origin loopback form.
        // The fixture emulates a browser's localhost context; no cookie or credential enters MCP.
        var cookies = login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]);
        submission.Headers.Add("Cookie", string.Join("; ", cookies));
        using var authenticated = await client.SendAsync(submission);
        var location = authenticated.Headers.Location;
        if (authenticated.StatusCode != HttpStatusCode.Redirect || location is null
            || location.GetLeftPart(UriPartial.Path) != redirect)
        {
            var failure = await authenticated.Content.ReadAsStringAsync();
            var message = Regex.Match(failure, "<p[^>]*class=\"[^\"]*instruction[^\"]*\"[^>]*>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value;
            var safeMessage = Regex.Replace(WebUtility.HtmlDecode(message), "<[^>]*>", string.Empty).Trim();
            throw new InvalidOperationException($"Keycloak user authorization failed with HTTP {(int)authenticated.StatusCode}: {safeMessage}");
        }

        var parameters = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query);
        if (parameters["state"] != state || !parameters.TryGetValue("code", out var code))
            throw new InvalidOperationException("Keycloak authorization response failed state validation.");

        using var tokenResponse = await client.PostAsync($"{Issuer}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["redirect_uri"] = redirect,
                ["code"] = code.ToString(),
                ["code_verifier"] = verifier
            }));
        tokenResponse.EnsureSuccessStatusCode();
        var tokens = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = tokens.GetProperty("access_token").GetString()!;
        var payload = token.Split('.')[1];
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/')
            .PadRight((payload.Length + 3) / 4 * 4, '=')));
        return (token, claims.RootElement.GetProperty("sub").GetString()!);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_relay is not null)
            await _relay;
        _listener.Stop();
        await Task.WhenAll(_connections);
        await _container.DisposeAsync();
        _stopping.Dispose();
    }

    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
