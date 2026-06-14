using NGB.Contracts.Security;

namespace NGB.Runtime.Security;

public interface INgbPermissionDefinitionSource
{
    Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct);
}
