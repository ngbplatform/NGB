using NGB.Application.Abstractions.Services;
using NGB.Contracts.Audit;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.Runtime.Security;

namespace NGB.Runtime.AuditLog;

public sealed class PermissionAwareAuditLogQueryService(
    AuditLogQueryService inner,
    INgbAccessChecker access,
    IDocumentRepository documents,
    ICatalogRepository catalogs)
    : IAuditLogQueryService
{
    public async Task<AuditLogPageDto> GetEntityAuditLogAsync(
        AuditEntityKind entityKind,
        Guid entityId,
        DateTime? afterOccurredAtUtc,
        Guid? afterAuditEventId,
        int limit,
        CancellationToken ct)
    {
        await RequireViewAsync(entityKind, entityId, ct);

        return await inner.GetEntityAuditLogAsync(
            entityKind,
            entityId,
            afterOccurredAtUtc,
            afterAuditEventId,
            limit,
            ct);
    }

    private async Task RequireViewAsync(AuditEntityKind entityKind, Guid entityId, CancellationToken ct)
    {
        if (await access.HasAsync(
                NgbResourceKinds.System,
                NgbPermissionResources.Audit,
                NgbPermissionActions.View,
                ct))
        {
            return;
        }

        switch (entityKind)
        {
            case AuditEntityKind.Document:
            {
                var document = await documents.GetAsync(entityId, ct);
                if (document is not null)
                {
                    await access.RequireAsync(
                        NgbResourceKinds.Document,
                        document.TypeCode,
                        NgbPermissionActions.ViewAudit,
                        ct);
                    return;
                }

                break;
            }
            case AuditEntityKind.Catalog:
            {
                var catalog = await catalogs.GetAsync(entityId, ct);
                if (catalog is not null)
                {
                    await access.RequireAsync(
                        NgbResourceKinds.Catalog,
                        catalog.CatalogCode,
                        NgbPermissionActions.ViewAudit,
                        ct);
                    return;
                }

                break;
            }
            case AuditEntityKind.ChartOfAccountsAccount:
                await access.RequireAsync(
                    NgbResourceKinds.Admin,
                    NgbPermissionResources.ChartOfAccounts,
                    NgbPermissionActions.View,
                    ct);
                return;
            case AuditEntityKind.Period:
                await access.RequireAsync(
                    NgbResourceKinds.Admin,
                    NgbPermissionResources.PeriodClosing,
                    NgbPermissionActions.View,
                    ct);
                return;
        }

        await access.RequireAsync(
            NgbResourceKinds.System,
            NgbPermissionResources.Audit,
            NgbPermissionActions.View,
            ct);
    }
}
