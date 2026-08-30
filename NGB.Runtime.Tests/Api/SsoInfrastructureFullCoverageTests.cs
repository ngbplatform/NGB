using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using NGB.Api.Models;
using NGB.Api.Sso;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class SsoInfrastructureFullCoverageTests
{
    [Fact]
    public async Task MemoryCacheTicketStore_covers_store_retrieve_renew_expiration_and_remove()
    {
        using var sut = new MemoryCacheTicketStore();
        var first = Ticket("first", expiresUtc: null);

        var key = await sut.StoreAsync(first);

        key.Should().StartWith("AuthSessionStore-");
        (await sut.RetrieveAsync(key)).Should().BeSameAs(first);

        var renewed = Ticket("renewed", DateTimeOffset.UtcNow.AddMinutes(5));
        await sut.RenewAsync(key, renewed);
        (await sut.RetrieveAsync(key)).Should().BeSameAs(renewed);

        await sut.RemoveAsync(key);
        (await sut.RetrieveAsync(key)).Should().BeNull();
        (await sut.RetrieveAsync("missing")).Should().BeNull();

        Action invalidCapacity = () => new MemoryCacheTicketStore(0);
        invalidCapacity.Should().Throw<ArgumentOutOfRangeException>();

        using var bounded = new MemoryCacheTicketStore(maximumSessionCount: 1);
        var evictedKey = await bounded.StoreAsync(Ticket("evicted", null));
        var retainedKey = await bounded.StoreAsync(Ticket("retained", null));
        (await bounded.RetrieveAsync(evictedKey)).Should().BeNull();
        (await bounded.RetrieveAsync(retainedKey)).Should().NotBeNull();

        using var concurrent = new MemoryCacheTicketStore(maximumSessionCount: 4);
        var hotKey = await concurrent.StoreAsync(Ticket("hot", null));
        var reads = Enumerable.Range(0, 10_000)
            .Select(_ => concurrent.RetrieveAsync(hotKey))
            .ToArray();
        (await Task.WhenAll(reads)).Should().OnlyContain(ticket => ticket != null);
        concurrent.TrackedSessionCount.Should().Be(1);
        concurrent.RecencyMetadataCount.Should().BeLessThanOrEqualTo(128);

        using var frequentlyRenewed = new MemoryCacheTicketStore(maximumSessionCount: 4);
        var renewedKey = await frequentlyRenewed.StoreAsync(Ticket("renewed-often", null));
        for (var index = 0; index < 1_000; index++)
            await frequentlyRenewed.RenewAsync(renewedKey, Ticket($"renewed-{index}", null));

        frequentlyRenewed.TrackedSessionCount.Should().Be(1);
        frequentlyRenewed.RecencyMetadataCount.Should().BeLessThanOrEqualTo(128);
        (await frequentlyRenewed.RetrieveAsync(renewedKey)).Should().NotBeNull();
    }

    [Fact]
    public void TokenExpiry_rejects_invalid_and_missing_expiry_tokens_and_reads_valid_expiry()
    {
        Action invalid = () => GetTokenExpiry("not-a-jwt");
        invalid.Should().Throw<TargetInvocationException>()
            .WithInnerException<NgbConfigurationViolationException>()
            .WithMessage("*valid JWT*");

        var noExpiry = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims: []));
        Action missingExpiry = () => GetTokenExpiry(noExpiry);
        missingExpiry.Should().Throw<TargetInvocationException>()
            .WithInnerException<NgbConfigurationViolationException>()
            .WithMessage("*expiry claim*");

        var expires = DateTime.UtcNow.AddMinutes(10);
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(expires: expires));
        GetTokenExpiry(token).Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Token_cache_requests_a_client_credentials_token_once_and_reuses_it_until_refresh_window()
    {
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var token = new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(expires: now.UtcDateTime.AddMinutes(10)));
        var handler = new TokenEndpointHandler(token);
        using var httpClient = new HttpClient(handler);
        var sut = new TokenCacheService(
            httpClient,
            new KeycloakApiClientSettings("https://identity.example", "platform", "api", "secret"),
            new FixedTimeProvider(now));

        var first = await sut.GetTokenAsync(CancellationToken.None);
        var second = await sut.GetTokenAsync(CancellationToken.None);

        first.Should().Be(token);
        second.Should().Be(token);
        handler.RequestCount.Should().Be(1);
        handler.RequestUri.Should().Be(
            "https://identity.example/realms/platform/protocol/openid-connect/token");
        handler.RequestBody.Should().Contain("grant_type=client_credentials")
            .And.Contain("client_id=api")
            .And.Contain("client_secret=secret");
    }

    [Fact]
    public async Task Token_cache_coalesces_cold_misses_and_cache_hits_do_not_wait_for_refresh_lock()
    {
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var token = new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(expires: now.UtcDateTime.AddMinutes(10)));
        var handler = new TokenEndpointHandler(token);
        using var httpClient = new HttpClient(handler);
        var sut = new TokenCacheService(
            httpClient,
            new KeycloakApiClientSettings("https://identity.example", "platform", "api", "secret"),
            new FixedTimeProvider(now));

        var coldResults = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => sut.GetTokenAsync(CancellationToken.None)));
        coldResults.Should().OnlyContain(value => value == token);
        handler.RequestCount.Should().Be(1);

        var gate = (SemaphoreSlim)typeof(TokenCacheService)
            .GetField("_semaphore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(sut)!;
        await gate.WaitAsync();
        try
        {
            var cached = await sut.GetTokenAsync(new CancellationToken(canceled: true));
            cached.Should().Be(token);
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public void KeycloakAdminException_and_settings_expose_safe_complete_context()
    {
        var withoutExtra = new KeycloakAdminClientException("users.get", 503);
        withoutExtra.ErrorCode.Should().Be(KeycloakAdminClientException.Code);
        withoutExtra.Context.Should().Contain("operation", "users.get").And.Contain("statusCode", 503);

        var withExtra = new KeycloakAdminClientException(
            "users.update",
            409,
            new Dictionary<string, object?>
            {
                ["keycloakError"] = "body_present",
                ["statusCode"] = 418
            });
        withExtra.Context.Should().Contain("operation", "users.update")
            .And.Contain("statusCode", 418)
            .And.Contain("keycloakError", "body_present");

        new KeycloakAdminClientSettings().Should().BeEquivalentTo(new
        {
            BaseUrl = string.Empty,
            Realm = string.Empty,
            ClientId = string.Empty,
            ClientSecret = string.Empty,
            AdminBatchConcurrency = 8,
            TotalRequestTimeout = TimeSpan.FromSeconds(30),
            AttemptTimeout = TimeSpan.FromSeconds(10),
            UserLookupCacheTtl = TimeSpan.FromMinutes(2),
            MissingUserCacheTtl = TimeSpan.FromSeconds(15),
            MaxCachedUserLookups = 20_000
        });
        new KeycloakApiClientSettings("url", "realm", "client", "secret")
            .Should().BeEquivalentTo(new { Url = "url", Realm = "realm", ClientId = "client", ClientSecret = "secret" });
    }

    private static AuthenticationTicket Ticket(string name, DateTimeOffset? expiresUtc)
    {
        var properties = new AuthenticationProperties { ExpiresUtc = expiresUtc };
        return new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "cookie")),
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static DateTime GetTokenExpiry(string token)
        => (DateTime)(typeof(TokenCacheService)
            .GetMethod("GetTokenExpiry", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [token])!);

    private sealed class TokenEndpointHandler(string token) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri?.ToString();
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":600}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
