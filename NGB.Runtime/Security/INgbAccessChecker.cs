namespace NGB.Runtime.Security;

public interface INgbAccessChecker
{
    Task<bool> HasAsync(string resourceKind, string resourceCode, string actionCode, CancellationToken ct);

    Task RequireAsync(string resourceKind, string resourceCode, string actionCode, CancellationToken ct);
}
