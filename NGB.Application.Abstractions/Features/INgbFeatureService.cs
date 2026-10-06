using NGB.Contracts.Features;

namespace NGB.Application.Abstractions.Features;

public interface INgbFeatureService
{
    Task<bool> IsEnabledAsync(string feature, CancellationToken ct);

    Task RequireAsync(string feature, CancellationToken ct);

    Task<IReadOnlyList<FeatureStateDto>> GetAllAsync(CancellationToken ct);
}
