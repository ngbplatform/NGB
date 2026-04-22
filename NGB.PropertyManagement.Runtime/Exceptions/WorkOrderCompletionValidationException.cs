using NGB.Core.Documents;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class WorkOrderCompletionValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static WorkOrderCompletionValidationException WorkOrderNotFound(Guid workOrderId, Guid? documentId = null)
        => new(
            message: "Selected work order was not found.",
            errorCode: "pm.work_order_completion.work_order.not_found",
            context: BuildContext(documentId, workOrderId: workOrderId, errors: new Dictionary<string, string[]>
            {
                ["work_order_id"] = ["Selected work order was not found."]
            }));

    public static WorkOrderCompletionValidationException WorkOrderMustBePosted(
        Guid workOrderId,
        Guid? documentId = null,
        DocumentStatus? actualStatus = null)
        => new(
            message: "Selected work order must be posted before creating a work order completion.",
            errorCode: "pm.work_order_completion.work_order.must_be_posted",
            context: BuildContext(documentId, workOrderId: workOrderId, status: actualStatus?.ToString(), errors: new Dictionary<string, string[]>
            {
                ["work_order_id"] = ["Selected work order must be posted before creating a work order completion."]
            }));

    public static WorkOrderCompletionValidationException WorkOrderAlreadyCompleted(
        Guid workOrderId,
        Guid? documentId = null)
        => new(
            message: "Selected work order already has a posted completion.",
            errorCode: "pm.work_order_completion.work_order.already_completed",
            context: BuildContext(documentId, workOrderId: workOrderId, errors: new Dictionary<string, string[]>
            {
                ["work_order_id"] = ["Selected work order already has a posted completion."]
            }));

    public static WorkOrderCompletionValidationException OutcomeInvalid(string? value, Guid? documentId = null)
        => new(
            message: "Outcome must be one of: Completed, Cancelled, Unable to complete.",
            errorCode: "pm.work_order_completion.outcome.invalid",
            context: BuildContext(documentId, outcome: value, errors: new Dictionary<string, string[]>
            {
                ["outcome"] = ["Outcome must be one of: Completed, Cancelled, Unable to complete."]
            }));

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid? documentId = null,
        Guid? workOrderId = null,
        string? outcome = null,
        string? status = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (documentId is not null)
            ctx["documentId"] = documentId.Value;
        if (workOrderId is not null)
            ctx["workOrderId"] = workOrderId.Value;
        if (outcome is not null)
            ctx["outcome"] = outcome;
        if (status is not null)
            ctx["workOrderStatus"] = status;
        if (errors is not null)
            ctx["errors"] = errors;
        return ctx;
    }
}
