using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class SsoDependencyInjectionHelperCoverageTests
{
    [Fact]
    public void Signing_key_and_client_id_helpers_cover_valid_invalid_trimmed_and_duplicate_values()
    {
        var key = Invoke<RsaSecurityKey>("GetSigningKey", new SecurityKeyParameters("AQAB", "AQIDBA"));
        key.Parameters.Exponent.Should().Equal(1, 0, 1);
        key.Parameters.Modulus.Should().Equal(1, 2, 3, 4);

        Action nullSettings = () => Invoke<string[]>("GetValidClientIds", (object?)null);
        nullSettings.Should().Throw<TargetInvocationException>()
            .WithInnerException<NgbArgumentRequiredException>();
        Action noClients = () => Invoke<string[]>("GetValidClientIds", new KeycloakSettings
        {
            ClientIds = [" ", ""]
        });
        noClients.Should().Throw<TargetInvocationException>()
            .WithInnerException<NgbConfigurationViolationException>();

        Invoke<string[]>("GetValidClientIds", new KeycloakSettings
        {
            ClientIds = [" web ", "web", "WEB", " "]
        }).Should().Equal("web", "WEB");
    }

    [Fact]
    public void Audience_validator_accepts_audience_authorized_party_or_client_id_and_rejects_every_other_shape()
    {
        var validator = Invoke<AudienceValidator>("BuildKeycloakAudienceValidator", (object)new[] { "web", "api" });
        var parameters = new TokenValidationParameters();
        var plain = Jwt(new Claim("unrelated", "value"));

        validator(["other", "web"], plain, parameters).Should().BeTrue();
        validator(["other"], Jwt(new Claim("azp", "api")), parameters).Should().BeTrue();
        validator(null, Jwt(new Claim("client_id", "web")), parameters).Should().BeTrue();
        validator([], Jwt(new Claim("azp", "wrong"), new Claim("client_id", "api")), parameters).Should().BeTrue();
        validator([], Jwt(new Claim("azp", "wrong"), new Claim("client_id", "wrong")), parameters).Should().BeFalse();
        validator([], plain, parameters).Should().BeFalse();

        Action nullToken = () => validator([], null!, parameters);
        nullToken.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Token_claim_reader_supports_jwt_json_web_token_raw_payloads_and_unknown_tokens()
    {
        ReadClaim(Jwt(new Claim("azp", " web ")), "azp").Should().Be((true, " web "));
        ReadClaim(Jwt(), "missing").Should().Be((false, string.Empty));

        var encoded = new JwtSecurityTokenHandler().WriteToken(Jwt(new Claim("client_id", "client")));
        ReadClaim(new JsonWebToken(encoded), "client_id").Should().Be((true, "client"));
        ReadClaim(new JsonWebToken(new JwtSecurityTokenHandler().WriteToken(Jwt(new Claim("empty", " ")))), "empty")
            .Should().Be((false, " "));
        ReadClaim(new JsonWebToken(new JwtSecurityTokenHandler().WriteToken(Jwt())), "missing")
            .Should().Be((false, string.Empty));
        ReadClaim(new UnknownSecurityToken(), "claim").Should().Be((false, string.Empty));

        using var stringDocument = JsonDocument.Parse("\"json-value\"");
        ReadRaw("text").Should().Be((true, "text"));
        ReadRaw(stringDocument.RootElement.Clone()).Should().Be((true, "json-value"));
        ReadRaw(42).Should().Be((true, "42"));
        ReadRaw(null).Should().Be((false, string.Empty));
        ReadRaw(" ").Should().Be((false, " "));
    }

    [Fact]
    public void Role_claim_enrichment_parses_direct_realm_and_resource_roles_and_deduplicates()
    {
        Action nullIdentity = () => InvokeVoid("AddKeycloakRoleClaims", null, new[] { "web" });
        nullIdentity.Should().Throw<TargetInvocationException>()
            .WithInnerException<NgbArgumentRequiredException>();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, "existing"),
            new Claim("role", " existing "),
            new Claim("roles", "direct"),
            new Claim("roles", " "),
            new Claim("realm_access", "{\"roles\":[\"realm\",\" \",17]}"),
            new Claim("realm_access", "not-json"),
            new Claim("realm_access", "{}"),
            new Claim("realm_access", "{\"roles\":{}}"),
            new Claim("resource_access", "{\"web\":{\"roles\":[\"client\",null]},\"ignored\":{\"roles\":[\"ignored\"]}}"),
            new Claim("resource_access", "not-json"),
            new Claim("resource_access", "[]"),
            new Claim("resource_access", "{\"web\":{}}"),
            new Claim("resource_access", "{\"web\":{\"roles\":{}}}")
        ], "test");

        InvokeVoid("AddKeycloakRoleClaims", identity, new[] { "web", "missing" });

        identity.FindAll(ClaimTypes.Role).Select(x => x.Value)
            .Should().BeEquivalentTo("existing", " existing ", "direct", "realm", "client");
    }

    [Fact]
    public void Realm_resource_and_role_array_readers_return_empty_for_malformed_shapes()
    {
        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromRealmAccess", "not-json").Should().BeEmpty();
        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromRealmAccess", "{}").Should().BeEmpty();
        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromRealmAccess", "{\"roles\":{}}").Should().BeEmpty();
        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromRealmAccess", "{\"roles\":[\"one\",1,\" \"]}")
            .Should().Equal("one");

        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromResourceAccess", "not-json", new[] { "web" })
            .Should().BeEmpty();
        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromResourceAccess", "[]", new[] { "web" })
            .Should().BeEmpty();
        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromResourceAccess", "{}", new[] { "web" })
            .Should().BeEmpty();
        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromResourceAccess", "{\"web\":{}}", new[] { "web" })
            .Should().BeEmpty();
        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromResourceAccess", "{\"web\":{\"roles\":{}}}", new[] { "web" })
            .Should().BeEmpty();
        Invoke<IEnumerable<string>>("ReadKeycloakRolesFromResourceAccess", "{\"web\":{\"roles\":[\"one\",false]}}", new[] { "web" })
            .Should().Equal("one");
    }

    [Theory]
    [InlineData(" Admin Console ", ".ngb.admin-console")]
    [InlineData("web-client", ".ngb.web-client")]
    [InlineData("---", ".ngb.admin-console")]
    [InlineData(" WEB.Client ", ".ngb.web-client")]
    public void Cookie_prefix_is_normalized_and_has_a_safe_fallback(string clientId, string expected)
        => Invoke<string>("BuildAdminConsoleCookiePrefix", clientId).Should().Be(expected);

    [Theory]
    [InlineData(null, "https", "admin.example", null, null)]
    [InlineData(" ", "https", "admin.example", null, null)]
    [InlineData("https://other.example/done", "http", "admin.example", null, "https://other.example/done")]
    [InlineData("http://other.example/done", "https", "admin.example", null, "http://other.example/done")]
    [InlineData("/done", "https", "admin.example", null, "https://admin.example/done")]
    [InlineData("done", "http", "localhost:5051", null, "http://localhost:5051/done")]
    [InlineData("done", "http", "localhost:5051", "https://public.example/", "https://public.example/done")]
    public void Redirect_uri_resolution_covers_null_absolute_relative_host_and_public_origin(
        string? redirect,
        string scheme,
        string host,
        string? publicOrigin,
        string? expected)
        => Invoke<string?>("ResolveAbsoluteRedirectUri", redirect, scheme, new HostString(host), publicOrigin)
            .Should().Be(expected);

    [Fact]
    public void Redirect_uri_resolution_rejects_non_http_absolute_uris()
    {
        Action act = () => Invoke<string?>(
            "ResolveAbsoluteRedirectUri",
            "ftp://files.example/done",
            "https",
            new HostString("admin.example"),
            null);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NgbConfigurationViolationException>()
            .WithMessage("*HTTP or HTTPS*");
    }

    private static JwtSecurityToken Jwt(params Claim[] claims) => new(claims: claims);

    private static (bool Success, string Value) ReadClaim(SecurityToken token, string claimType)
    {
        object?[] arguments = [token, claimType, null];
        var success = (bool)Method("TryReadSecurityTokenStringClaim").Invoke(null, arguments)!;
        return (success, (string)arguments[2]!);
    }

    private static (bool Success, string Value) ReadRaw(object? raw)
    {
        object?[] arguments = [raw, null];
        var success = (bool)Method("TryReadRawTokenValue").Invoke(null, arguments)!;
        return (success, (string)arguments[1]!);
    }

    private static T Invoke<T>(string methodName, params object?[] arguments)
        => (T)Method(methodName).Invoke(null, arguments)!;

    private static void InvokeVoid(string methodName, params object?[] arguments)
        => Method(methodName).Invoke(null, arguments);

    private static MethodInfo Method(string methodName)
        => typeof(NGB.Hosting.AspNetCore.Identity.DependencyInjection).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new MissingMethodException(typeof(NGB.Hosting.AspNetCore.Identity.DependencyInjection).FullName, methodName);

    private sealed class UnknownSecurityToken : SecurityToken
    {
        public override string Id => "unknown";
        public override string Issuer => "issuer";
        public override SecurityKey? SecurityKey { get; } = null;
        public override SecurityKey? SigningKey { get; set; }
        public override DateTime ValidFrom => DateTime.MinValue;
        public override DateTime ValidTo => DateTime.MaxValue;
    }
}
