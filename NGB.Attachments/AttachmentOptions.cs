namespace NGB.Attachments;

/// <summary>Bounded attachment limits. Pending uploads reserve capacity until completion or expiry.</summary>
public sealed class AttachmentOptions
{
    public long MaxSizeBytes { get; set; } = 50 * 1024 * 1024;
    public int MaxActivePerObject { get; set; } = 100;
    public TimeSpan UploadLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan DownloadLifetime { get; set; } = TimeSpan.FromMinutes(3);
    public TimeSpan PendingStaleAge { get; set; } = TimeSpan.FromHours(24);
    public int ExpirationBatchSize { get; set; } = 25;

    public bool IsValid() => MaxSizeBytes is >= 1 and <= 5L * 1024 * 1024 * 1024
        && MaxActivePerObject is >= 1 and <= 1000
        && UploadLifetime >= TimeSpan.FromMinutes(1)
        && UploadLifetime <= TimeSpan.FromHours(1)
        && DownloadLifetime >= TimeSpan.FromSeconds(30)
        && DownloadLifetime <= TimeSpan.FromMinutes(15)
        && PendingStaleAge >= UploadLifetime + TimeSpan.FromMinutes(5)
        && PendingStaleAge <= TimeSpan.FromDays(7)
        && ExpirationBatchSize is >= 1 and <= 100;
}
