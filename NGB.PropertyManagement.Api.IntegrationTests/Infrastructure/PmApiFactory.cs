using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

public sealed class PmApiFactory : WebApplicationFactory<Program>
{
    private const string TestSeqServerUrl = "http://127.0.0.1:5341";

    private static readonly Uri DefaultBaseAddress = new("https://localhost");
    private static readonly object EnvironmentOverridesLock = new();

    private readonly PmIntegrationFixture _fixture;
    private readonly string _connectionString;
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;
    private readonly IReadOnlyDictionary<string, string?> _effectiveConfiguration;
    private readonly Dictionary<string, string?> _previousEnvironmentVariables = new(StringComparer.Ordinal);
    private bool _environmentOverridesRestored;

    public PmApiFactory(PmIntegrationFixture fixture, IReadOnlyDictionary<string, string?>? configurationOverrides = null)
        : this(fixture, fixture?.ConnectionString ?? throw new ArgumentNullException(nameof(fixture)), configurationOverrides)
    {
    }

    public PmApiFactory(
        PmIntegrationFixture fixture,
        string connectionString,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = connectionString;
        _configurationOverrides = configurationOverrides ?? new Dictionary<string, string?>();
        _effectiveConfiguration = BuildEffectiveConfiguration();

        ApplyProcessEnvironmentOverrides(_effectiveConfiguration);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(_effectiveConfiguration);
        });
    }

    protected override void Dispose(bool disposing)
    {
        RestoreProcessEnvironmentOverrides();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        RestoreProcessEnvironmentOverrides();
        await base.DisposeAsync();
    }

    public new HttpClient CreateClient() => CreateAuthenticatedClient();

    public new HttpClient CreateClient(WebApplicationFactoryClientOptions options) => CreateAuthenticatedClient(options);

    public HttpClient CreateAuthenticatedClient(
        WebApplicationFactoryClientOptions? options = null,
        PmKeycloakTestUser? user = null,
        string? clientId = null,
        Guid? platformUserId = null)
    {
        var clientOptions = options ?? new WebApplicationFactoryClientOptions
        {
            BaseAddress = DefaultBaseAddress
        };

        var client = base.CreateClient(clientOptions);
        var accessToken = _fixture.Keycloak.GetAccessTokenAsync(
                user ?? _fixture.Keycloak.DefaultUser,
                clientId ?? PmKeycloakTestClients.DefaultApiTestClient)
            .GetAwaiter()
            .GetResult();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (platformUserId.HasValue)
            client.DefaultRequestHeaders.Add("X-Platform-User-Id", platformUserId.Value.ToString());

        return client;
    }

    public HttpClient CreateAnonymousClient(WebApplicationFactoryClientOptions? options = null)
    {
        return base.CreateClient(options ?? new WebApplicationFactoryClientOptions
        {
            BaseAddress = DefaultBaseAddress
        });
    }

    private IReadOnlyDictionary<string, string?> BuildEffectiveConfiguration()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = _connectionString,
            ["KeycloakSettings:Issuer"] = _fixture.Keycloak.Issuer,
            ["KeycloakSettings:RequireHttpsMetadata"] = bool.FalseString,
            ["Serilog:WriteTo:1:Args:serverUrl"] = TestSeqServerUrl,
            ["KeycloakSettings:ClientIds:0"] = PmKeycloakTestClients.Api,
            ["KeycloakSettings:ClientIds:1"] = PmKeycloakTestClients.WebClient,
            ["KeycloakSettings:ClientIds:2"] = PmKeycloakTestClients.Tester
        };

        foreach (var pair in _configurationOverrides)
        {
            overrides[pair.Key] = pair.Value;
        }

        return overrides;
    }

    private void ApplyProcessEnvironmentOverrides(IReadOnlyDictionary<string, string?> configuration)
    {
        lock (EnvironmentOverridesLock)
        {
            foreach (var pair in configuration)
            {
                var environmentKey = pair.Key.Replace(":", "__", StringComparison.Ordinal);

                if (!_previousEnvironmentVariables.ContainsKey(environmentKey))
                    _previousEnvironmentVariables[environmentKey] = Environment.GetEnvironmentVariable(environmentKey);

                Environment.SetEnvironmentVariable(environmentKey, pair.Value);
            }
        }
    }

    private void RestoreProcessEnvironmentOverrides()
    {
        lock (EnvironmentOverridesLock)
        {
            if (_environmentOverridesRestored)
                return;

            foreach (var pair in _previousEnvironmentVariables)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            _environmentOverridesRestored = true;
        }
    }
}
