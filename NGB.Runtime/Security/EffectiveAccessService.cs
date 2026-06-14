using NGB.Contracts.Security;
using NGB.Core.Security;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;

namespace NGB.Runtime.Security;

public sealed class EffectiveAccessService(
    IPlatformUserRepository users,
    IUserAccessVersionRepository versions,
    IPermissionSnapshotRepository permissions,
    PermissionDefinitionRegistry definitions)
    : IEffectiveAccessService
{
    public async Task<EffectiveAccessDto> GetEffectiveAccessAsync(Guid userId, CancellationToken ct)
    {
        _ = await users.GetByIdAsync(userId, ct) ?? throw new SecurityUserNotFoundException(userId);

        var version = await versions.GetAsync(userId, ct);
        var granted = (await permissions.GetEffectivePermissionsAsync(userId, ct)).ToHashSet();
        var allDefinitions = await definitions.GetAllAsync(ct);

        var groups = allDefinitions
            .GroupBy(x => x.Group)
            .Select(group => new EffectiveAccessGroupDto(
                group.Key,
                group
                    .GroupBy(x => new { x.ResourceKind, x.ResourceCode })
                    .Select(resource =>
                    {
                        var grantedActions = resource
                            .Where(definition => granted.Contains(new NgbPermissionKey(
                                definition.ResourceKind,
                                definition.ResourceCode,
                                definition.ActionCode)))
                            .Select(static x => x.ActionCode)
                            .OrderBy(static x => x, StringComparer.Ordinal)
                            .ToArray();

                        var first = resource.First();
                        return new EffectiveAccessResourceDto(
                            first.ResourceKind,
                            first.ResourceCode,
                            DisplayName: BuildDisplayName(resource),
                            Actions: grantedActions);
                    })
                    .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new EffectiveAccessDto(userId, version?.Version ?? 1, groups);
    }

    private static string BuildDisplayName(IEnumerable<PermissionDefinitionDto> definitions)
    {
        var first = definitions.First();
        var marker = first.DisplayName.IndexOf(':', StringComparison.Ordinal);

        return marker > 0
            ? first.DisplayName[..marker]
            : first.ResourceCode;
    }
}
