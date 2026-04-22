namespace NGB.BackgroundJobs.Catalog;

internal sealed class PlatformBackgroundJobCatalogContributor : IBackgroundJobCatalogContributor
{
    public IReadOnlyCollection<string> GetJobIds() => PlatformJobCatalog.All;
}
