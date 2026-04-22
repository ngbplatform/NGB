using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace NGB.Api.CurrentUser;

/// <summary>
/// Resolves the current execution actor from the authenticated ASP.NET request principal.
/// </summary>
public sealed class HttpCurrentActorContext(IHttpContextAccessor httpContextAccessor) : ICurrentActorContext
{
    public ActorIdentity? Current => BuildActorIdentity(httpContextAccessor.HttpContext?.User);

    private static ActorIdentity? BuildActorIdentity(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var authSubject = FindFirstNonEmpty(principal, ClaimTypes.NameIdentifier, "sub");
        if (authSubject is null)
            return null;

        var email = FindFirstNonEmpty(principal, ClaimTypes.Email, "email");
        var displayName = ResolveDisplayName(principal, email);
        var isActive = ResolveIsActive(principal);

        return new ActorIdentity(
            AuthSubject: authSubject,
            Email: email,
            DisplayName: displayName,
            IsActive: isActive);
    }

    private static string? ResolveDisplayName(ClaimsPrincipal principal, string? email)
    {
        var explicitName = FindFirstNonEmpty(principal, "name");
        if (explicitName is not null)
            return explicitName;

        var givenName = FindFirstNonEmpty(principal, ClaimTypes.GivenName, "given_name");
        var familyName = FindFirstNonEmpty(principal, ClaimTypes.Surname, "family_name");

        var fullName = string.Join(" ", new[] { givenName, familyName }.Where(static x => !string.IsNullOrWhiteSpace(x)));
        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName.Trim();

        var preferredUsername = FindFirstNonEmpty(principal, "preferred_username");
        if (preferredUsername is not null)
            return preferredUsername;

        var identityName = principal.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(identityName))
            return identityName.Trim();

        return email;
    }

    private static bool ResolveIsActive(ClaimsPrincipal principal)
    {
        if (TryReadBooleanClaim(principal, "active", out var isActive))
            return isActive;

        if (TryReadBooleanClaim(principal, "is_active", out isActive))
            return isActive;

        if (TryReadBooleanClaim(principal, "enabled", out isActive))
            return isActive;

        return true;
    }

    private static bool TryReadBooleanClaim(ClaimsPrincipal principal, string claimType, out bool value)
    {
        var raw = FindFirstNonEmpty(principal, claimType);
        if (raw is null)
        {
            value = false;
            return false;
        }

        if (bool.TryParse(raw, out value))
            return true;

        if (string.Equals(raw, "1", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        if (string.Equals(raw, "0", StringComparison.Ordinal))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static string? FindFirstNonEmpty(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
