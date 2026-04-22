using NGB.Contracts.Admin;

namespace NGB.Application.Abstractions.Services;

/// <summary>
/// Contributes one or more main menu groups.
///
/// The platform composes the final main menu by aggregating all contributors.
/// This allows vertical solutions and platform modules to extend navigation
/// without replacing a single "god" menu service.
/// </summary>
public interface IMainMenuContributor
{
    Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct);
}
