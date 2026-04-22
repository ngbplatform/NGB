using System.ComponentModel.DataAnnotations;
using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Reporting;

public enum MaintenanceQueueState
{
    [Display(Name = "Requested")]
    Requested = 1,
    
    [Display(Name = "Work ordered")]
    WorkOrdered = 2,
    
    [Display(Name = "Overdue")]
    Overdue = 3
}

public static class MaintenanceQueueStateExtensions
{
    public static bool TryParse(string? raw, out MaintenanceQueueState state)
    {
        state = default;
        var value = raw?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal))
        {
            case "REQUESTED":
                state = MaintenanceQueueState.Requested;
                return true;
            case "WORKORDERED":
            case "WORKORDER":
            case "OPEN":
                state = MaintenanceQueueState.WorkOrdered;
                return true;
            case "OVERDUE":
                state = MaintenanceQueueState.Overdue;
                return true;
            default:
                return false;
        }
    }
}

public sealed record MaintenanceQueueQuery(
    DateOnly AsOfUtc,
    Guid? BuildingId,
    Guid? PropertyId,
    Guid? CategoryId,
    Guid? AssignedPartyId,
    string? Priority,
    MaintenanceQueueState? QueueState,
    int Offset,
    int Limit)
{
    public void EnsureInvariant()
    {
        if (Offset < 0)
            throw new NgbArgumentOutOfRangeException(nameof(Offset), Offset, "Offset must be zero or positive.");

        if (Limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(Limit), Limit, "Limit must be positive.");
    }
}

public sealed record MaintenanceQueueRow(
    Guid RequestId,
    string RequestDisplay,
    string Subject,
    DateOnly RequestedAtUtc,
    int AgingDays,
    Guid BuildingId,
    string BuildingDisplay,
    Guid PropertyId,
    string PropertyDisplay,
    Guid CategoryId,
    string CategoryDisplay,
    string Priority,
    Guid RequestedByPartyId,
    string RequestedByDisplay,
    Guid? WorkOrderId,
    string? WorkOrderDisplay,
    Guid? AssignedPartyId,
    string? AssignedPartyDisplay,
    DateOnly? DueByUtc,
    MaintenanceQueueState QueueState)
{
    public void EnsureInvariant()
    {
        if (RequestId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(RequestId), "RequestId must not be empty.");

        if (string.IsNullOrWhiteSpace(RequestDisplay))
            throw new NgbArgumentInvalidException(nameof(RequestDisplay), "Request display is required.");

        if (string.IsNullOrWhiteSpace(Subject))
            throw new NgbArgumentInvalidException(nameof(Subject), "Subject is required.");

        if (AgingDays < 0)
            throw new NgbArgumentInvalidException(nameof(AgingDays), "AgingDays must not be negative.");

        if (BuildingId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(BuildingId), "BuildingId must not be empty.");

        if (PropertyId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(PropertyId), "PropertyId must not be empty.");

        if (CategoryId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(CategoryId), "CategoryId must not be empty.");

        if (RequestedByPartyId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(RequestedByPartyId), "RequestedByPartyId must not be empty.");

        if (string.IsNullOrWhiteSpace(BuildingDisplay)
            || string.IsNullOrWhiteSpace(PropertyDisplay)
            || string.IsNullOrWhiteSpace(CategoryDisplay)
            || string.IsNullOrWhiteSpace(RequestedByDisplay)
            || string.IsNullOrWhiteSpace(Priority))
        {
            throw new NgbArgumentInvalidException(nameof(BuildingDisplay), "Display fields are required.");
        }

        if (WorkOrderId is null)
        {
            if (QueueState != MaintenanceQueueState.Requested)
                throw new NgbArgumentInvalidException(nameof(QueueState), "Rows without a work order must be in Requested state.");

            if (WorkOrderDisplay is not null || AssignedPartyId is not null || AssignedPartyDisplay is not null || DueByUtc is not null)
                throw new NgbArgumentInvalidException(nameof(WorkOrderId), "Requested rows must not contain work-order details.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(WorkOrderDisplay))
                throw new NgbArgumentInvalidException(nameof(WorkOrderDisplay), "Work-order display is required when WorkOrderId is set.");
        }
    }
}

public sealed record MaintenanceQueuePage(IReadOnlyList<MaintenanceQueueRow> Rows, int Total)
{
    public void EnsureInvariant()
    {
        if (Rows is null)
            throw new NgbArgumentRequiredException(nameof(Rows));

        if (Total < 0)
            throw new NgbArgumentInvalidException(nameof(Total), "Total must not be negative.");

        foreach (var row in Rows)
        {
            row.EnsureInvariant();
        }
    }
}
