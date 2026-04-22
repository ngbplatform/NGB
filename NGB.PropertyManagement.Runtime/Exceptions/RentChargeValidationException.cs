using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class RentChargeValidationException(
    string message,
    string errorCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbValidationException(message, errorCode, context)
{
    public static RentChargeValidationException LeaseNotFound(Guid leaseId)
        => new(
            message: "Selected lease was not found.",
            errorCode: $"{PropertyManagementCodes.RentCharge}.lease.not_found",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["leaseId"] = leaseId,
                ["field"] = "lease_id",
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["lease_id"] = ["Selected lease was not found."]
                }
            });

    public static RentChargeValidationException LeaseMarkedForDeletion(Guid leaseId)
        => new(
            message: "Selected lease is marked for deletion.",
            errorCode: $"{PropertyManagementCodes.RentCharge}.lease.deleted",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["leaseId"] = leaseId,
                ["field"] = "lease_id",
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["lease_id"] = ["Selected lease is marked for deletion."]
                }
            });

    public static RentChargeValidationException AmountMustBePositive(decimal amount)
        => new(
            message: "Amount must be positive.",
            errorCode: $"{PropertyManagementCodes.RentCharge}.amount.must_be_positive",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["amount"] = amount,
                ["field"] = "amount",
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["amount"] = ["Amount must be positive."]
                }
            });

    public static RentChargeValidationException PeriodRangeInvalid(DateOnly fromInclusive, DateOnly toInclusive)
        => new(
            message: "Period end must be on or after period start.",
            errorCode: $"{PropertyManagementCodes.RentCharge}.period.invalid_range",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["periodFromUtc"] = fromInclusive,
                ["periodToUtc"] = toInclusive,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["period_from_utc"] = ["Period start must be on or before period end."],
                    ["period_to_utc"] = ["Period end must be on or after period start."]
                }
            });

    public static RentChargeValidationException PeriodOutsideLeaseTerm(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DateOnly leaseStartOnUtc,
        DateOnly? leaseEndOnUtc)
        => new(
            message: BuildOutsideLeaseMessage(leaseStartOnUtc, leaseEndOnUtc),
            errorCode: $"{PropertyManagementCodes.RentCharge}.period.outside_lease_term",
            context: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["periodFromUtc"] = fromInclusive,
                ["periodToUtc"] = toInclusive,
                ["leaseStartOnUtc"] = leaseStartOnUtc,
                ["leaseEndOnUtc"] = leaseEndOnUtc,
                ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["period_from_utc"] = [BuildOutsideLeaseFieldMessage(leaseStartOnUtc, leaseEndOnUtc)],
                    ["period_to_utc"] = [BuildOutsideLeaseFieldMessage(leaseStartOnUtc, leaseEndOnUtc)]
                }
            });

    private static string BuildOutsideLeaseMessage(DateOnly leaseStartOnUtc, DateOnly? leaseEndOnUtc)
        => leaseEndOnUtc is null
            ? $"Rent charge period must start on or after lease start ({leaseStartOnUtc:MM/dd/yyyy})."
            : $"Rent charge period must stay within lease term ({leaseStartOnUtc:MM/dd/yyyy} → {leaseEndOnUtc.Value:MM/dd/yyyy}).";

    private static string BuildOutsideLeaseFieldMessage(DateOnly leaseStartOnUtc, DateOnly? leaseEndOnUtc)
        => leaseEndOnUtc is null
            ? $"Rent charge period must start on or after lease start ({leaseStartOnUtc:MM/dd/yyyy})."
            : $"Rent charge period must stay within lease term ({leaseStartOnUtc:MM/dd/yyyy} → {leaseEndOnUtc.Value:MM/dd/yyyy}).";
}
