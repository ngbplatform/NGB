namespace NGB.BackgroundJobs.Catalog;

public interface IBackgroundJobCatalog
{
    IReadOnlyList<string> All { get; }
}
