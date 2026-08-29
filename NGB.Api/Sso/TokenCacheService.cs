using System.IdentityModel.Tokens.Jwt;
using IdentityModel;
using IdentityModel.Client;
using NGB.Api.Models;
using NGB.Tools.Exceptions;

namespace NGB.Api.Sso;

public class TokenCacheService
{
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly HttpClient? _httpClient;
    private readonly KeycloakApiClientSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    
    private string? _cachedToken;
    private DateTime _tokenExpiry;

    public TokenCacheService(IHttpClientFactory httpClientFactory, KeycloakApiClientSettings settings, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    internal TokenCacheService(HttpClient httpClient, KeycloakApiClientSettings settings, TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    private static DateTime GetTokenExpiry(string token)
    {
        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(token))
            throw new NgbConfigurationViolationException("Keycloak access token must be a valid JWT.");

        var jwtToken = handler.ReadJwtToken(token);

        var expiryUnix = jwtToken.Payload.Expiration;
        if (expiryUnix == null)
            throw new NgbConfigurationViolationException("Keycloak access token must include an expiry claim.");

        var expiryDateTime = DateTimeOffset.FromUnixTimeSeconds(expiryUnix.Value).UtcDateTime;
        
        return expiryDateTime;
    }

    private async Task<TokenResponse> GetNewTokenAsync(CancellationToken cancellationToken)
    {
        var request = new TokenRequest
        {
            GrantType = OidcConstants.GrantTypes.ClientCredentials,
            ClientId = _settings.ClientId,
            ClientSecret = _settings.ClientSecret,
            RequestUri = new Uri(_settings.Url + $"/realms/{_settings.Realm}/protocol/openid-connect/token")
        };

        var client = _httpClientFactory?.CreateClient(KeycloakHttpClientNames.Token) ?? _httpClient!;
        var tokenResponse = await client.RequestTokenAsync(request, cancellationToken);

        return tokenResponse;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedToken) && _timeProvider.GetUtcNow() < _tokenExpiry)
                return _cachedToken;

            var tokenResponse = await GetNewTokenAsync(cancellationToken);
            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiry = GetTokenExpiry(_cachedToken).AddSeconds(-60);

            return _cachedToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
