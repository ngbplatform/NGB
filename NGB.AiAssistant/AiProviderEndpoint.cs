namespace NGB.AiAssistant;

public static class AiProviderEndpoint
{
    public static bool IsValid(string value, bool allowLocalHttp = false)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && string.IsNullOrEmpty(uri.UserInfo)
           && string.IsNullOrEmpty(uri.Query)
           && string.IsNullOrEmpty(uri.Fragment)
           && (uri.Scheme == Uri.UriSchemeHttps
               || allowLocalHttp
               && uri.Scheme == Uri.UriSchemeHttp
               && (uri.IsLoopback || uri.IdnHost.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase)));
}
