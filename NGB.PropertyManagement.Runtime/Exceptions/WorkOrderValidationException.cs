using NGB.Core.Documents;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class WorkOrderValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static WorkOrderValidationException RequestNotFound(Guid requestId, Guid? documentId = null)
        => new(
            message: "Selected maintenance request was not found.",
            errorCode: "pm.work_order.request.not_found",
            context: BuildContext(documentId, requestId: requestId, errors: new Dictionary<string, string[]>
            {
                ["request_id"] = ["Selected maintenance request was not found."]
            }));

    public static WorkOrderValidationException RequestMustBePosted(
        Guid requestId,
        Guid? documentId = null,
        DocumentStatus? actualStatus = null)
        => new(
            message: "Selected maintenance request must be posted before creating a work order.",
            errorCode: "pm.work_order.request.must_be_posted",
            context: BuildContext(documentId, requestId: requestId, status: actualStatus?.ToString(), errors: new Dictionary<string, string[]>
            {
                ["request_id"] = ["Selected maintenance request must be posted before creating a work order."]
            }));

    public static WorkOrderValidationException AssignedPartyNotFound(Guid partyId, Guid? documentId = null)
        => new(
            message: "Assigned party was not found.",
            errorCode: "pm.work_order.assigned_party.not_found",
            context: BuildContext(documentId, assignedPartyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["assigned_party_id"] = ["Assigned party was not found."]
            }));

    public static WorkOrderValidationException AssignedPartyDeleted(Guid partyId, Guid? documentId = null)
        => new(
            message: "Assigned party is marked for deletion.",
            errorCode: "pm.work_order.assigned_party.deleted",
            context: BuildContext(documentId, assignedPartyId: partyId, errors: new Dictionary<string, string[]>
            {
                ["assigned_party_id"] = ["Assigned party is marked for deletion."]
            }));

    public static WorkOrderValidationException CostResponsibilityInvalid(string? value, Guid? documentId = null)
        => new(
            message: "Cost responsibility must be one of: Owner, Tenant, Company, Unknown.",
            errorCode: "pm.work_order.cost_responsibility.invalid",
            context: BuildContext(documentId, costResponsibility: value, errors: new Dictionary<string, string[]>
            {
                ["cost_responsibility"] = ["Cost responsibility must be one of: Owner, Tenant, Company, Unknown."]
            }));

    private static IReadOnlyDictionary<string, object?> BuildContext(
        Guid? documentId = null,
        Guid? requestId = null,
        Guid? assignedPartyId = null,
        string? costResponsibility = null,
        string? status = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var ctx = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (documentId is not null)
            ctx["documentId"] = documentId.Value;
        if (requestId is not null)
            ctx["requestId"] = requestId.Value;
        if (assignedPartyId is not null)
            ctx["assignedPartyId"] = assignedPartyId.Value;
        if (costResponsibility is not null)
            ctx["costResponsibility"] = costResponsibility;
        if (status is not null)
            ctx["requestStatus"] = status;
        if (errors is not null)
            ctx["errors"] = errors;
        return ctx;
    }
}
