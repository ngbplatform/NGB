namespace NGB.Mcp.AspNetCore;

public sealed class NgbMcpOptions
{
    public bool Enabled { get; set; }
    public string Resource { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string AuthenticationScheme { get; set; } = "Bearer";
    public string[] AllowedOrigins { get; set; } = [];
    public bool AllowLocalhostHttp { get; set; }
    public int ConcurrentRequests { get; set; } = 24;
    public int RequestsPerMinute { get; set; } = 120;
    public int RequestTimeoutSeconds { get; set; } = 45;

    /// <summary>Positive UTF-8 byte budget for the whole HTTP body, including the JSON-RPC envelope.</summary>
    public int MaxRequestBytes { get; set; } = 128 * 1024;

    public string? PublicUiOrigin { get; set; }
    public Dictionary<string, string> UiRoutes { get; set; } = new(StringComparer.Ordinal);
}
