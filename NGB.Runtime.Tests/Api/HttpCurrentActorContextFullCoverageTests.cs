using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NGB.Api.CurrentUser;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class HttpCurrentActorContextFullCoverageTests
{
    [Fact]
    public void Current_returns_null_without_an_authenticated_subject()
    {
        new HttpCurrentActorContext(new HttpContextAccessor()).Current.Should().BeNull();
        Context(new ClaimsPrincipal()).Current.Should().BeNull();
        Context(new ClaimsPrincipal(new ClaimsIdentity())).Current.Should().BeNull();
        Context(Principal(new Claim(ClaimTypes.Email, "user@example.com"))).Current.Should().BeNull();
    }

    [Fact]
    public void Current_prefers_standard_subject_explicit_name_and_normalized_unique_roles()
    {
        var sut = Context(Principal(
            new Claim(ClaimTypes.NameIdentifier, "  subject-1  "),
            new Claim("sub", "subject-2"),
            new Claim(ClaimTypes.Email, " user@example.com "),
            new Claim("name", " Explicit User "),
            new Claim(ClaimTypes.Role, " Admin "),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(ClaimTypes.Role, " "),
            new Claim("active", "false")));

        sut.Current.Should().NotBeNull();
        var actor = sut.Current!;
        actor.AuthSubject.Should().Be("subject-1");
        actor.Email.Should().Be("user@example.com");
        actor.DisplayName.Should().Be("Explicit User");
        actor.IsActive.Should().BeFalse();
        actor.AuthRoles.Should().BeEquivalentTo("Admin");
    }

    [Fact]
    public void Current_uses_oidc_fallback_claims_and_combines_partial_names()
    {
        var sut = Context(Principal(
            new Claim(ClaimTypes.NameIdentifier, " "),
            new Claim("sub", "oidc-subject"),
            new Claim(ClaimTypes.Email, " "),
            new Claim("email", "oidc@example.com"),
            new Claim(ClaimTypes.GivenName, " Ada "),
            new Claim(ClaimTypes.Surname, " "),
            new Claim("family_name", " Lovelace "),
            new Claim("is_active", "1")));

        sut.Current.Should().NotBeNull();
        var actor = sut.Current!;
        actor.AuthSubject.Should().Be("oidc-subject");
        actor.Email.Should().Be("oidc@example.com");
        actor.DisplayName.Should().Be("Ada Lovelace");
        actor.IsActive.Should().BeTrue();
        actor.AuthRoles.Should().BeEmpty();
    }

    [Fact]
    public void Current_resolves_every_display_name_and_activity_fallback()
    {
        Actor(new Claim(ClaimTypes.GivenName, "Given")).DisplayName.Should().Be("Given");
        Actor(new Claim(ClaimTypes.Surname, "Family")).DisplayName.Should().Be("Family");
        Actor(new Claim("preferred_username", " preferred ")).DisplayName.Should().Be("preferred");
        Actor(name: " Identity User ").DisplayName.Should().Be("Identity User");
        Actor(new Claim(ClaimTypes.Email, "fallback@example.com"), name: " ").DisplayName
            .Should().Be("fallback@example.com");
        Actor(name: " ").DisplayName.Should().BeNull();

        Actor(new Claim("active", "TRUE")).IsActive.Should().BeTrue();
        Actor(new Claim("active", "invalid"), new Claim("is_active", "0")).IsActive.Should().BeFalse();
        Actor(new Claim("active", "invalid"), new Claim("is_active", "invalid"), new Claim("enabled", "1"))
            .IsActive.Should().BeTrue();
        Actor(new Claim("active", "invalid"), new Claim("is_active", "invalid"), new Claim("enabled", "0"))
            .IsActive.Should().BeFalse();
        Actor(new Claim("active", "invalid"), new Claim("is_active", "invalid"), new Claim("enabled", "invalid"))
            .IsActive.Should().BeTrue();
        Actor().IsActive.Should().BeTrue();
    }

    private static NGB.Runtime.CurrentActor.ActorIdentity Actor(params Claim[] claims)
        => Actor(claims, name: null);

    private static NGB.Runtime.CurrentActor.ActorIdentity Actor(Claim claim, string? name)
        => Actor([claim], name);

    private static NGB.Runtime.CurrentActor.ActorIdentity Actor(string? name)
        => Actor([], name);

    private static NGB.Runtime.CurrentActor.ActorIdentity Actor(Claim[] claims, string? name)
    {
        var allClaims = new[] { new Claim(ClaimTypes.NameIdentifier, "subject") }.Concat(claims);
        var identity = new ClaimsIdentity(allClaims, "test", ClaimTypes.Name, ClaimTypes.Role);
        if (name is not null)
            identity.AddClaim(new Claim(ClaimTypes.Name, name));

        return Context(new ClaimsPrincipal(identity)).Current!;
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));

    private static HttpCurrentActorContext Context(ClaimsPrincipal principal)
    {
        var http = new DefaultHttpContext { User = principal };
        return new HttpCurrentActorContext(new HttpContextAccessor { HttpContext = http });
    }
}
