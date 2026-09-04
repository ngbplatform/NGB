using NGB.Tools.Exceptions;

namespace NGB.Hosting.AspNetCore.Identity;

public sealed class AdminConsoleAuthOptions
{
    public string? CallbackPath { get; set; }

    public string? PublicOrigin { get; set; }

    public bool ForceHttpsRedirectUri { get; set; } = true;

    internal void ValidateAndNormalize()
    {
        CallbackPath = string.IsNullOrWhiteSpace(CallbackPath)
            ? null
            : NormalizePath(CallbackPath);

        PublicOrigin = string.IsNullOrWhiteSpace(PublicOrigin)
            ? null
            : NormalizePublicOrigin(PublicOrigin);
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim();

        if (!normalized.StartsWith('/'))
            normalized = $"/{normalized}";

        if (normalized.Length > 1)
            normalized = normalized.TrimEnd('/');

        return normalized;
    }

    private static string NormalizePublicOrigin(string publicOrigin)
    {
        var normalized = publicOrigin.Trim().TrimEnd('/');

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new NgbConfigurationViolationException(
                "Admin console public origin must be an absolute http/https URL.",
                new Dictionary<string, object?>
                {
                    ["publicOrigin"] = publicOrigin
                });
        }

        return normalized;
    }
}
