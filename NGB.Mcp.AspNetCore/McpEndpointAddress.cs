using NGB.Hosting.AspNetCore;
using NGB.Tools.Http;

namespace NGB.Mcp.AspNetCore;

/// <summary>Resolves the configured public address once, when the host maps its MCP route.</summary>
internal sealed class McpEndpointAddress
{
    private readonly string _publicApiUrl;
    private readonly string _resourceOverride;
    private Uri? _resource;

    internal McpEndpointAddress(NgbMcpOptions options, NgbHostingOptions hosting)
    {
        _resourceOverride = options.Resource;
        _publicApiUrl = hosting.PublicApiUrl;
        var value = string.IsNullOrEmpty(_resourceOverride)
            ? _publicApiUrl
            : _resourceOverride;

        if (!HttpEndpointUri.IsSecureAbsolute(value, options.AllowLocalhostHttp))
        {
            throw new InvalidOperationException("MCP requires a secure Ngb:Hosting:PublicApiUrl "
                + "or an explicit Mcp:Resource URL. Local HTTP requires Mcp:AllowLocalhostHttp.");
        }

        if (!string.IsNullOrEmpty(_resourceOverride) && new Uri(_resourceOverride).AbsolutePath == "/")
            throw new InvalidOperationException("Mcp:Resource must include the MCP endpoint path.");
    }

    internal Uri Resource
        => _resource ?? throw new InvalidOperationException("MapNgbMcp must be called before starting the MCP host.");

    internal Uri MetadataUri => new(Resource, "/.well-known/oauth-protected-resource" + Resource.AbsolutePath);

    internal void Map(string pattern)
    {
        if (_resource is not null)
            throw new InvalidOperationException("The MCP endpoint is already mapped.");

        if (string.IsNullOrWhiteSpace(pattern)
            || !pattern.StartsWith('/')
            || pattern == "/"
            || pattern.Contains("//", StringComparison.Ordinal)
            || pattern.Contains("..", StringComparison.Ordinal)
            || pattern.IndexOfAny(['{', '}', '?', '#', '%', '\\']) >= 0
            || pattern.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The MCP endpoint must be a literal absolute route path.", nameof(pattern));
        }

        if (string.IsNullOrEmpty(_resourceOverride))
        {
            _resource = new Uri(_publicApiUrl.TrimEnd('/') + pattern);
        }
        else
        {
            var resource = new Uri(_resourceOverride);

            // An explicit resource retains the existing contract: map its complete path.
            if (!string.Equals(resource.AbsolutePath, pattern, StringComparison.Ordinal))
                throw new InvalidOperationException("MCP endpoint path must match the configured canonical resource URI.");

            _resource = resource;
        }
    }
}
