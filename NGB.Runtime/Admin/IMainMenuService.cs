using NGB.Contracts.Admin;

namespace NGB.Runtime.Admin;

public interface IMainMenuService
{
    Task<MainMenuDto> GetMainMenuAsync(CancellationToken ct);
}
