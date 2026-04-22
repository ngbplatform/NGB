using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class LeaseOverlapsAnotherPostedLeaseException(
    Guid propertyId,
    Guid conflictingLeaseId,
    DateOnly startOnUtc,
    DateOnly? endOnUtc,
    DateOnly conflictingStartOnUtc,
    DateOnly? conflictingEndOnUtc)
    : NgbConflictException(
        message: "Lease overlaps another Posted lease for the same property.",
        errorCode: "pm.lease.overlap",
        context: new Dictionary<string, object?>
        {
            ["propertyId"] = propertyId,
            ["leaseId"] = conflictingLeaseId,
            ["startOnUtc"] = startOnUtc.ToString("yyyy-MM-dd"),
            ["endOnUtc"] = endOnUtc?.ToString("yyyy-MM-dd"),
            ["conflictingStartOnUtc"] = conflictingStartOnUtc.ToString("yyyy-MM-dd"),
            ["conflictingEndOnUtc"] = conflictingEndOnUtc?.ToString("yyyy-MM-dd")
        });
