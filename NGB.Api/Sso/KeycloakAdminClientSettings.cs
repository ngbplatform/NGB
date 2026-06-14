namespace NGB.Api.Sso;

public sealed record KeycloakAdminClientSettings
{
    public string BaseUrl { get; init; } = string.Empty;

    public string Realm { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;
}
