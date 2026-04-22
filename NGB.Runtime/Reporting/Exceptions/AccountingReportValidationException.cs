using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting.Exceptions;

public sealed class AccountingReportValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    private const string CashFlowIndirectReportCode = "accounting.cash_flow_statement_indirect";

    public static AccountingReportValidationException CashFlowIndirectReconciliationFailed(
        decimal beginningCash,
        decimal endingCash,
        decimal operating,
        decimal investing,
        decimal financing)
    {
        var message =
            $"Cash Flow Statement (Indirect) failed reconciliation. beginningCash={beginningCash}, endingCash={endingCash}, operating={operating}, investing={investing}, financing={financing}. " +
            "Verify cash-flow account classifications and non-cash operating adjustment tags.";

        return new(
            message,
            "accounting.validation.cash_flow_indirect.reconciliation_failed",
            BuildContext("report", message, new Dictionary<string, object?>
            {
                ["reportCode"] = CashFlowIndirectReportCode,
                ["beginningCash"] = beginningCash,
                ["endingCash"] = endingCash,
                ["operating"] = operating,
                ["investing"] = investing,
                ["financing"] = financing
            }));
    }

    public static AccountingReportValidationException CashFlowIndirectUnclassifiedCash(string details, int rowCount)
    {
        var message =
            "Cash Flow Statement (Indirect) found cash movements against unclassified balance-sheet counterparties. " +
            "Assign CashFlowRole/CashFlowLineCode to the affected accounts. " +
            $"Rows: {details}";

        return new(
            message,
            "accounting.validation.cash_flow_indirect.unclassified_cash",
            BuildContext("report", message, new Dictionary<string, object?>
            {
                ["reportCode"] = CashFlowIndirectReportCode,
                ["rowCount"] = rowCount,
                ["rows"] = details
            }));
    }

    private static IReadOnlyDictionary<string, object?> BuildContext(
        string field,
        string message,
        IReadOnlyDictionary<string, object?> extra)
    {
        var ctx = new Dictionary<string, object?>(extra, StringComparer.Ordinal)
        {
            ["field"] = field,
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message]
            }
        };

        return ctx;
    }
}
