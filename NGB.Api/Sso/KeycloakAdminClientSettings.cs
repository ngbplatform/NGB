namespace NGB.Api.Sso;

public sealed record KeycloakAdminClientSettings
{
    public string BaseUrl { get; init; } = string.Empty;

    public string Realm { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public int AdminBatchConcurrency { get; init; } = 8;

    public int MaxConcurrentAdminRequests { get; init; } = 16;

    public int MaxQueuedAdminRequests { get; init; } = 256;

    public int MaxPendingUserLookups { get; init; } = 128;

    public long MaxResponseContentBytes { get; init; } = 1_048_576;

    public TimeSpan TotalRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan UserLookupCacheTtl { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan MissingUserCacheTtl { get; init; } = TimeSpan.FromSeconds(15);

    public int MaxCachedUserLookups { get; init; } = 20_000;
}
