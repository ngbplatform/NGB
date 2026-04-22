using NGB.Accounting.Accounts;
using NGB.Tools.Exceptions;

namespace NGB.Trade.Runtime.Documents.Validation;

internal static class TradeAccountingValidationGuards
{
    public static async Task EnsureCashAccountAsync(
        Guid cashAccountId,
        string fieldPath,
        IChartOfAccountsProvider charts,
        CancellationToken ct)
    {
        if (cashAccountId == Guid.Empty)
            throw new NgbArgumentInvalidException(fieldPath, $"{fieldPath} is required.");

        var chart = await charts.GetAsync(ct);
        if (!chart.TryGet(cashAccountId, out var account) || account is null)
            throw new NgbArgumentInvalidException(fieldPath, "Referenced cash / bank account is not available.");

        if (account.Type != AccountType.Asset || account.StatementSection != StatementSection.Assets)
        {
            throw new NgbArgumentInvalidException(
                fieldPath,
                "Selected cash / bank account must be an asset account in the Assets section.");
        }

        if (account.DimensionRules.Any(x => x.IsRequired))
        {
            throw new NgbArgumentInvalidException(
                fieldPath,
                "Selected cash / bank account must not require dimensions.");
        }
    }
}
