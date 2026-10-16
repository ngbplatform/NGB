using System.Collections.Frozen;
using System.Text.RegularExpressions;
using NGB.Tools.Http;

namespace NGB.Mcp;

/// <summary>Maps authorized source references to explicitly configured UI routes on one trusted origin.</summary>
public sealed class ConfiguredMcpNgbLinkResolver : IMcpNgbLinkResolver
{
    private readonly Uri _origin;
    private readonly FrozenDictionary<string, string> _routes;

    public ConfiguredMcpNgbLinkResolver(
        Uri origin,
        IReadOnlyDictionary<string, string> routes,
        bool allowLocalhostHttp = false)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(routes);

        if (!origin.IsAbsoluteUri
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || origin.AbsolutePath != "/"
            || !(origin.Scheme == "https" || allowLocalhostHttp && origin.Scheme == "http" && HttpEndpointUri.IsLocalhost(origin)))
        {
            throw new ArgumentException("MCP links require a trusted HTTPS UI origin.", nameof(origin));
        }

        foreach (var (kind, route) in routes)
        {
            if (kind is not ("catalog" or "document" or "generalJournalEntry" or "report")
                || string.IsNullOrWhiteSpace(route)
                || !Regex.IsMatch(route, "^/[a-zA-Z0-9_./{}-]+$", RegexOptions.CultureInvariant)
                || route.StartsWith("//", StringComparison.Ordinal) || route.Contains("..", StringComparison.Ordinal)
                || route.Replace("{typeCode}", string.Empty, StringComparison.Ordinal)
                    .Replace("{id}", string.Empty, StringComparison.Ordinal).IndexOfAny(['{', '}']) >= 0
                || (kind == "report" ? route.Contains("{id}", StringComparison.Ordinal) : !route.Contains("{id}", StringComparison.Ordinal)))
            {
                throw new ArgumentException("MCP routes must be local paths with supported source placeholders.", nameof(routes));
            }
        }

        _origin = origin;
        _routes = routes.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public Uri? Resolve(string kind, string typeCode, Guid? id)
    {
        if (!_routes.TryGetValue(kind, out var route)
            || string.IsNullOrWhiteSpace(typeCode) || typeCode.Length > 128
            || !Regex.IsMatch(typeCode, "^[a-zA-Z0-9_][a-zA-Z0-9_.-]*$", RegexOptions.CultureInvariant)
            || kind != "report" && (!id.HasValue || id.Value == Guid.Empty))
        {
            return null;
        }

        var path = route.Replace("{typeCode}", Uri.EscapeDataString(typeCode), StringComparison.Ordinal)
            .Replace("{id}", id?.ToString("D") ?? string.Empty, StringComparison.Ordinal);

        return new Uri(_origin, path);
    }
}
