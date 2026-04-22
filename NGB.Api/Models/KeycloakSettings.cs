namespace NGB.Api.Models;

public record SecurityKeyParameters(string Exponent, string Modulus);

public sealed record KeycloakSettings
{
    public string Issuer { get; init; } = string.Empty;

    public IEnumerable<string> ClientIds { get; init; } = [];

    public bool RequireHttpsMetadata { get; init; } = true;
}
