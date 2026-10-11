namespace NGB.Hosting.AspNetCore.Identity;

public record SecurityKeyParameters(string Exponent, string Modulus);

public sealed record KeycloakSettings
{
    public string Issuer { get; init; } = string.Empty;

    /// <summary>
    /// Optional discovery URL reachable from the host's private network.
    /// The public issuer remains the trusted token issuer and browser authority.
    /// </summary>
    public string? MetadataAddress { get; init; }

    public IEnumerable<string> ClientIds { get; init; } = [];

    public bool RequireHttpsMetadata { get; init; } = true;
}
