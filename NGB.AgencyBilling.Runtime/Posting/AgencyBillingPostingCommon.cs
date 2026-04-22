using NGB.AgencyBilling.Documents;
using NGB.Core.Dimensions;
using NGB.Tools.Extensions;

namespace NGB.AgencyBilling.Runtime.Posting;

internal static class AgencyBillingPostingCommon
{
    private static readonly Guid ClientDimensionId = DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Client}");
    private static readonly Guid ProjectDimensionId = DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Project}");
    private static readonly Guid TeamMemberDimensionId = DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.TeamMember}");
    private static readonly Guid ServiceItemDimensionId = DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.ServiceItem}");
    private static readonly Guid ArOpenItemDimensionId = DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.ArOpenItemDimensionCode}");

    public static DateTime ToOccurredAtUtc(DateOnly date)
        => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    public static DimensionBag ProjectBag(Guid clientId, Guid projectId)
        => new(
        [
            new DimensionValue(ClientDimensionId, clientId),
            new DimensionValue(ProjectDimensionId, projectId)
        ]);

    public static DimensionBag ArOpenItemBag(Guid clientId, Guid projectId, Guid salesInvoiceId)
        => new(
        [
            new DimensionValue(ClientDimensionId, clientId),
            new DimensionValue(ProjectDimensionId, projectId),
            new DimensionValue(ArOpenItemDimensionId, salesInvoiceId)
        ]);

    public static DimensionBag TimeLedgerBag(Guid clientId, Guid projectId, Guid teamMemberId, Guid? serviceItemId)
    {
        var items = new List<DimensionValue>
        {
            new(ClientDimensionId, clientId),
            new(ProjectDimensionId, projectId),
            new(TeamMemberDimensionId, teamMemberId)
        };

        if (serviceItemId is { } resolvedServiceItemId && resolvedServiceItemId != Guid.Empty)
            items.Add(new DimensionValue(ServiceItemDimensionId, resolvedServiceItemId));

        return new DimensionBag(items);
    }

    public static IReadOnlyDictionary<string, decimal> BuildProjectTimeResources(
        decimal hours,
        bool billable,
        decimal billableAmount,
        decimal costAmount)
        => new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["hours_total"] = RoundScale4(hours),
            ["billable_hours"] = billable ? RoundScale4(hours) : 0m,
            ["non_billable_hours"] = billable ? 0m : RoundScale4(hours),
            ["billable_amount"] = billable ? RoundScale4(billableAmount) : 0m,
            ["cost_amount"] = RoundScale4(costAmount)
        };

    public static IReadOnlyDictionary<string, decimal> BuildUnbilledResources(decimal hoursDelta, decimal amountDelta)
        => new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["hours_open"] = RoundScale4(hoursDelta),
            ["amount_open"] = RoundScale4(amountDelta)
        };

    public static IReadOnlyDictionary<string, decimal> BuildProjectBillingStatusResources(
        decimal billedAmountDelta,
        decimal collectedAmountDelta,
        decimal outstandingArAmountDelta)
        => new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["billed_amount"] = RoundScale4(billedAmountDelta),
            ["collected_amount"] = RoundScale4(collectedAmountDelta),
            ["outstanding_ar_amount"] = RoundScale4(outstandingArAmountDelta)
        };

    public static decimal ResolveTimesheetLineAmount(AgencyBillingTimesheetLine line)
        => line.LineAmount ?? RoundScale4(line.Hours * (line.BillingRate ?? 0m));

    public static decimal ResolveTimesheetLineCostAmount(AgencyBillingTimesheetLine line)
        => line.LineCostAmount ?? RoundScale4(line.Hours * (line.CostRate ?? 0m));

    public static decimal ResolveSalesInvoiceLineAmount(AgencyBillingSalesInvoiceLine line)
        => line.LineAmount != 0m ? RoundScale4(line.LineAmount) : RoundScale4(line.QuantityHours * line.Rate);

    public static decimal RoundScale4(decimal value)
        => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}
