namespace NGB.PostgreSql.Reporting;

/// <summary>Provider fetch settings; independent of interactive UI page limits.</summary>
internal static class PostgresReportStreamingDefaults
{
    // Keep each server-cursor fetch bounded without a round trip for every row.
    public const int FetchBatchSize = 500;
    public const int CommandTimeoutSeconds = 300;
}
