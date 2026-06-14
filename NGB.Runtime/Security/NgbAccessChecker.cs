using NGB.Core.Security;

namespace NGB.Runtime.Security;

public sealed class NgbAccessChecker(IPermissionSnapshotProvider snapshots) : INgbAccessChecker
{
    public Task<PermissionSnapshot> GetSnapshotAsync(CancellationToken ct) => snapshots.GetCurrentAsync(ct);

    public async Task<bool> HasAsync(string resourceKind, string resourceCode, string actionCode, CancellationToken ct)
    {
        var permission = new NgbPermissionKey(resourceKind, resourceCode, actionCode);
        var snapshot = await GetSnapshotAsync(ct);
        return snapshot.Has(permission);
    }

    public async Task RequireAsync(string resourceKind, string resourceCode, string actionCode, CancellationToken ct)
    {
        var permission = new NgbPermissionKey(resourceKind, resourceCode, actionCode);
        var snapshot = await GetSnapshotAsync(ct);
        if (!snapshot.Has(permission))
            throw new NgbPermissionDeniedException(permission);
    }
}
