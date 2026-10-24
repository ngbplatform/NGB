using NGB.Core.Security;
using NGB.Mcp;
using NGB.Runtime.Security;

namespace ExternalMcp.Tests;

public sealed record CurrentUserRequest;
public sealed record CurrentUserResult(string Subject, bool Active);

public sealed class CurrentUserRead(INgbAccessChecker access) : IMcpReadToolHandler<CurrentUserRequest, CurrentUserResult>
{
    public async Task<CurrentUserResult> ExecuteAsync(CurrentUserRequest request, CancellationToken ct)
    {
        var snapshot = await access.GetSnapshotAsync(ct);
        return new CurrentUserResult(snapshot.AuthSubject!, snapshot.IsActive);
    }
}

public sealed class CurrentUserContributor : IMcpReadToolContributor
{
    public void Contribute(IMcpReadToolRegistryBuilder builder)
        => builder.Add<CurrentUserRequest, CurrentUserResult, CurrentUserRead>(
            "custom_current_user", "Read the authenticated user identity through the existing NGB permission boundary.",
            [NgbSystemPermissions.AuditView]);
}
