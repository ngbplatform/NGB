using Microsoft.FeatureManagement;
using NGB.Application.Abstractions.Features;
using NGB.Contracts.Features;
using NGB.Core.Features;

namespace NGB.Runtime.Features;

internal sealed class NgbFeatureService(NgbFeatureRegistry registry, IVariantFeatureManagerSnapshot features)
    : INgbFeatureService
{
    public async Task<bool> IsEnabledAsync(string feature, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return registry.Contains(feature) && await features.IsEnabledAsync(feature, ct);
    }

    public async Task RequireAsync(string feature, CancellationToken ct)
    {
        if (!await IsEnabledAsync(feature, ct))
            throw new NgbFeatureDisabledException(feature);
    }

    public async Task<IReadOnlyList<FeatureStateDto>> GetAllAsync(CancellationToken ct)
    {
        var result = new List<FeatureStateDto>();

        foreach (var definition in registry.Definitions)
        {
            result.Add(new FeatureStateDto(
                definition.Code,
                definition.DisplayName,
                definition.Group,
                await IsEnabledAsync(definition.Code, ct)));
        }

        return result;
    }
}
