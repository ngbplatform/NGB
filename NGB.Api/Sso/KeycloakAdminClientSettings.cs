namespace NGB.Api.Sso;

public sealed record KeycloakAdminClientSettings
{
    public string BaseUrl { get; init; } = string.Empty;

    public string Realm { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public int AdminBatchConcurrency { get; init; } = 8;

    public TimeSpan TotalRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan UserLookupCacheTtl { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan MissingUserCacheTtl { get; init; } = TimeSpan.FromSeconds(15);

    public int MaxCachedUserLookups { get; init; } = 20_000;
}
