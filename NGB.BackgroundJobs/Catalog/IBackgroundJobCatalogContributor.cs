namespace NGB.BackgroundJobs.Catalog;

public interface IBackgroundJobCatalogContributor
{
    IReadOnlyCollection<string> GetJobIds();
}
