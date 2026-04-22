using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;

namespace NGB.Runtime.Admin;

/// <summary>
/// Platform-level admin navigation items for Accounting.
/// </summary>
public sealed class AccountingAdminMainMenuContributor : IMainMenuContributor
{
    public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        var group = new MainMenuGroupDto(
            Label: "Setup & Controls",
            Items:
            [
                new MainMenuItemDto(
                    Kind: "admin",
                    Code: "chart-of-accounts",
                    Label: "Chart of Accounts",
                    Route: "/admin/chart-of-accounts",
                    Icon: "book-open",
                    Ordinal: 20),
                new MainMenuItemDto(
                    Kind: "admin",
                    Code: "accounting.period_closing",
                    Label: "Period Close",
                    Route: "/admin/accounting/period-closing",
                    Icon: "calendar-check",
                    Ordinal: 60),
                new MainMenuItemDto(
                    Kind: "admin",
                    Code: "accounting.posting_log",
                    Label: "Posting Log",
                    Route: "/admin/accounting/posting-log",
                    Icon: "history",
                    Ordinal: 70),
                new MainMenuItemDto(
                    Kind: "admin",
                    Code: "accounting.consistency",
                    Label: "Integrity Checks",
                    Route: "/admin/accounting/consistency",
                    Icon: "shield-check",
                    Ordinal: 80)
            ],
            Ordinal: 70,
            Icon: "settings");

        return Task.FromResult<IReadOnlyList<MainMenuGroupDto>>([group]);
    }
}
