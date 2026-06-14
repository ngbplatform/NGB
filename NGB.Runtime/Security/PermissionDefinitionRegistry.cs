using NGB.Contracts.Security;

namespace NGB.Runtime.Security;

public sealed class PermissionDefinitionRegistry(IEnumerable<INgbPermissionDefinitionSource> sources) : IDisposable
{
    private readonly INgbPermissionDefinitionSource[] _sources = sources.ToArray();
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private IReadOnlyList<PermissionDefinitionDto>? _cachedDefinitions;

    public async Task<IReadOnlyList<PermissionDefinitionDto>> GetAllAsync(CancellationToken ct)
    {
        if (_cachedDefinitions is { } cached)
            return cached;

        await _cacheGate.WaitAsync(ct);
        try
        {
            if (_cachedDefinitions is { } cachedAfterWait)
                return cachedAfterWait;

            _cachedDefinitions = await BuildAllAsync(ct);
            return _cachedDefinitions;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    public void Dispose() => _cacheGate.Dispose();

    private async Task<IReadOnlyList<PermissionDefinitionDto>> BuildAllAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, PermissionDefinitionDto>(StringComparer.Ordinal);

        foreach (var source in _sources)
        {
            var definitions = await source.GetDefinitionsAsync(ct);
            foreach (var definition in definitions)
            {
                var normalized = Normalize(definition);
                var key = $"{normalized.ResourceKind}.{normalized.ResourceCode}.{normalized.ActionCode}";
                result.TryAdd(key, normalized);
            }
        }

        return result.Values
            .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ResourceKind, StringComparer.Ordinal)
            .ThenBy(x => x.ResourceCode, StringComparer.Ordinal)
            .ThenBy(x => x.ActionCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static PermissionDefinitionDto Normalize(PermissionDefinitionDto definition)
        => definition with
        {
            ResourceKind = definition.ResourceKind.Trim().ToLowerInvariant(),
            ResourceCode = definition.ResourceCode.Trim().ToLowerInvariant(),
            ActionCode = definition.ActionCode.Trim().ToLowerInvariant(),
            DisplayName = definition.DisplayName.Trim(),
            Group = definition.Group.Trim()
        };
}
