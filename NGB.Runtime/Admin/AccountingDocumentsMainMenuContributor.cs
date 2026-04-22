using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;

namespace NGB.Runtime.Admin;

/// <summary>
/// Platform-level accounting navigation items that are not vertical-specific.
/// </summary>
public sealed class AccountingDocumentsMainMenuContributor : IMainMenuContributor
{
    public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        var group = new MainMenuGroupDto(
            Label: "Accounting",
            Items:
            [
                new MainMenuItemDto(
                    Kind: "page",
                    Code: AccountingDocumentTypeCodes.GeneralJournalEntry,
                    Label: "Journal Entries",
                    Route: "/documents/accounting.general_journal_entry",
                    Icon: "book-open",
                    Ordinal: 10)
            ],
            Ordinal: 60,
            Icon: "calculator");

        return Task.FromResult<IReadOnlyList<MainMenuGroupDto>>([group]);
    }
}
