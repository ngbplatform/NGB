namespace NGB.Runtime.Security;

public interface IPermissionSnapshotProvider
{
    Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct);
}
