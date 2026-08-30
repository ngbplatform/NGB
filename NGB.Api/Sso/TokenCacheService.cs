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
    
    private TokenCacheEntry? _cacheEntry;

    internal sealed record TokenCacheEntry(string Token, DateTime ExpiresAtUtc);

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
        if (TryGetCachedToken(out var cachedToken))
            return cachedToken;

        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            // Another waiter may have refreshed the token while this caller was queued.
            if (TryGetCachedToken(out cachedToken))
                return cachedToken;

            var tokenResponse = await GetNewTokenAsync(cancellationToken);
            var refreshedToken = tokenResponse.AccessToken;
            var refreshedExpiry = GetTokenExpiry(refreshedToken).AddSeconds(-60);

            // Publish token and expiry as one immutable snapshot. Separate fields can let
            // a lock-free reader pair an expired old token with the new future expiry.
            Volatile.Write(ref _cacheEntry, new TokenCacheEntry(refreshedToken, refreshedExpiry));

            return refreshedToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool TryGetCachedToken(out string token)
    {
        var observed = Volatile.Read(ref _cacheEntry);
        if (observed is not null
            && !string.IsNullOrWhiteSpace(observed.Token)
            && _timeProvider.GetUtcNow() < observed.ExpiresAtUtc)
        {
            token = observed.Token;
            return true;
        }

        token = string.Empty;
        return false;
    }
}
