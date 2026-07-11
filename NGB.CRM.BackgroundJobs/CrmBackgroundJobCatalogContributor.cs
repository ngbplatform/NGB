using NGB.BackgroundJobs.Catalog;

namespace NGB.CRM.BackgroundJobs;

internal sealed class CrmBackgroundJobCatalogContributor : IBackgroundJobCatalogContributor
{
    public IReadOnlyCollection<string> GetJobIds() =>
    [
        PlatformJobCatalog.PlatformSchemaValidate,
        PlatformJobCatalog.AuditHealth,
        PlatformJobCatalog.ReferenceRegistersEnsureSchema
    ];
}
