using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

public sealed class PmKeycloakFixture : IAsyncDisposable
{
    private const string RealmName = "ngb-pm-tests";
    private const int HttpPort = 8080;
    private static readonly Uri DefaultBaseAddress = new("https://localhost");

    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<TokenCacheKey, CachedAccessToken> _tokenCache = new();

    private IContainer? _container;
    private HttpClient? _httpClient;

    public string Realm => RealmName;

    public string BaseUrl { get; private set; } = string.Empty;

    public string Issuer { get; private set; } = string.Empty;

    public PmKeycloakTestUser DefaultUser => PmKeycloakTestUsers.Admin;

    public Uri DefaultClientBaseAddress => DefaultBaseAddress;

    public async Task InitializeAsync()
    {
        var realmImportFile = ResolveRealmImportFile();

        _container = new ContainerBuilder("quay.io/keycloak/keycloak:26.5.6")
            .WithName($"ngb-pm-keycloak-{Guid.NewGuid():N}")
            .WithPortBinding(HttpPort, true)
            .WithEnvironment("KEYCLOAK_ADMIN", "admin")
            .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
            .WithCommand("start-dev", "--hostname-strict=false", "--import-realm")
            .WithResourceMapping(realmImportFile, new FileInfo("/opt/keycloak/data/import/ngb-pm-tests-realm.json"))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(HttpPort)
                .ForPath($"/realms/{RealmName}/.well-known/openid-configuration")))
            .Build();

        await _container.StartAsync();

        BaseUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}";
        Issuer = $"{BaseUrl}/realms/{RealmName}";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl)
        };
    }

    public async Task<string> GetAccessTokenAsync(
        PmKeycloakTestUser? user = null,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        user ??= DefaultUser;
        clientId ??= PmKeycloakTestClients.DefaultApiTestClient;

        var cacheKey = new TokenCacheKey(user.Username, clientId);

        if (TryGetValidCachedToken(cacheKey, out var cachedToken))
            return cachedToken;

        await _tokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (TryGetValidCachedToken(cacheKey, out cachedToken))
                return cachedToken;

            EnsureInitialized();

            var formFields = new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = clientId,
                ["username"] = user.Username,
                ["password"] = user.Password,
                ["scope"] = "openid profile email"
            };

            if (PmKeycloakTestClients.TryGetClientSecret(clientId, out var clientSecret))
                formFields["client_secret"] = clientSecret;

            using var response = await _httpClient!.PostAsync(
                $"/realms/{RealmName}/protocol/openid-connect/token",
                new FormUrlEncodedContent(formFields),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new InvalidOperationException("Keycloak token endpoint returned no payload.");

            var expiresAtUtc = ReadExpiryUtc(payload.AccessToken);
            _tokenCache[cacheKey] = new CachedAccessToken(payload.AccessToken, expiresAtUtc);
            return payload.AccessToken;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        _tokenSemaphore.Dispose();

        if (_container is not null)
            await _container.DisposeAsync();
    }

    private static FileInfo ResolveRealmImportFile()
    {
        var file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Keycloak", "ngb-pm-tests-realm.json"));
        if (file.Exists)
            return file;

        throw new FileNotFoundException(
            "The Keycloak realm import file was not found in the test output.",
            file.FullName);
    }

    private bool TryGetValidCachedToken(TokenCacheKey cacheKey, out string accessToken)
    {
        accessToken = string.Empty;

        if (!_tokenCache.TryGetValue(cacheKey, out var cached))
            return false;

        if (cached.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddSeconds(30))
            return false;

        accessToken = cached.AccessToken;
        return true;
    }

    private static DateTimeOffset ReadExpiryUtc(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        return jwt.ValidTo == DateTime.MinValue
            ? DateTimeOffset.UtcNow.AddMinutes(5)
            : new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
    }

    private void EnsureInitialized()
    {
        if (_httpClient is null)
            throw new InvalidOperationException("Keycloak fixture is not initialized.");
    }

    private sealed record TokenCacheKey(string Username, string ClientId);

    private sealed record CachedAccessToken(string AccessToken, DateTimeOffset ExpiresAtUtc);

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}

public sealed record PmKeycloakTestUser(string Username, string Password);

public static class PmKeycloakTestUsers
{
    public static readonly PmKeycloakTestUser Admin = new("pm-admin", "PmAdmin!2026");

    public static readonly PmKeycloakTestUser Analyst = new("pm-analyst", "PmAnalyst!2026");
}

public static class PmKeycloakTestClients
{
    private const string TesterSecret = "ngb-pm-tester-secret-2026";

    public const string Api = "ngb-api-main";
    public const string WebClient = "ngb-web-client";
    public const string Tester = "ngb-tester";
    public const string Watchdog = "ngb-watchdog";
    public const string BackgroundJobs = "ngb-background-jobs";
    public const string DefaultApiTestClient = Tester;

    public static readonly IReadOnlyList<string> ApiClientIds = [Api, WebClient, Tester];

    public static bool TryGetClientSecret(string clientId, out string clientSecret)
    {
        if (string.Equals(clientId, Tester, StringComparison.Ordinal))
        {
            clientSecret = TesterSecret;
            return true;
        }

        clientSecret = string.Empty;
        return false;
    }
}
