namespace NGB.Tools.Http;

/// <summary>URI checks for configured public HTTP endpoints, without DNS resolution.</summary>
public static class HttpEndpointUri
{
    public static bool IsSecureAbsolute(string value, bool allowLocalhostHttp = false)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && (uri.Scheme == Uri.UriSchemeHttps || allowLocalhostHttp && uri.Scheme == Uri.UriSchemeHttp && IsLocalhost(uri));

    public static bool IsLocalhost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri)
            return false;

        // RFC 6761 reserves localhost and every name beneath it for local use.
        var host = uri.IdnHost.TrimEnd('.');

        return uri.IsLoopback
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }
}
