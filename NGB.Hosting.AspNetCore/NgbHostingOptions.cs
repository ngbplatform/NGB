namespace NGB.Hosting.AspNetCore;

/// <summary>Public deployment addresses shared by the API host's integrations.</summary>
public sealed class NgbHostingOptions
{
    public const string ConfigurationSection = "Ngb:Hosting";

    /// <summary>
    /// Canonical external API base URL, optionally including a reverse-proxy path prefix.
    /// This address does not configure Kestrel listeners and must not come from request headers.
    /// </summary>
    public string PublicApiUrl { get; set; } = string.Empty;
}
