using NGB.Accounting.Accounts;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Documents.Validation;

internal static class AgencyBillingAccountingValidationGuards
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
            throw new NgbArgumentInvalidException(fieldPath, "Selected cash / bank account was not found.");

        if (account.Type != AccountType.Asset)
            throw new NgbArgumentInvalidException(fieldPath, "Selected cash / bank account must be an Asset account.");

        if (account.DimensionRules.Any(x => x.IsRequired))
            throw new NgbArgumentInvalidException(fieldPath, "Selected cash / bank account cannot require dimensions.");
    }
}
