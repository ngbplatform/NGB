using System.IdentityModel.Tokens.Jwt;
using IdentityModel;
using IdentityModel.Client;
using NGB.Api.Models;
using NGB.Tools.Exceptions;

namespace NGB.Api.Sso;

public class TokenCacheService(HttpClient httpClient, KeycloakApiClientSettings settings, TimeProvider timeProvider)
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    
    private string? _cachedToken;
    private DateTime _tokenExpiry;

    private static DateTime GetTokenExpiry(string token)
    {
        var handler = new JwtSecurityTokenHandler();

        if (handler.ReadToken(token) is not JwtSecurityToken jwtToken)
            throw new NgbConfigurationViolationException("Keycloak access token must be a valid JWT.");

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
            ClientId = settings.ClientId,
            ClientSecret = settings.ClientSecret,
            RequestUri = new Uri(settings.Url + $"/realms/{settings.Realm}/protocol/openid-connect/token")
        };

        var tokenResponse = await httpClient.RequestTokenAsync(request, cancellationToken);
        return tokenResponse;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedToken) && timeProvider.GetUtcNow() < _tokenExpiry)
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
