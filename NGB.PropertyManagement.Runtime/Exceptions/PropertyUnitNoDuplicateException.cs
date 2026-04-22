using NGB.Tools.Exceptions;

namespace NGB.PropertyManagement.Runtime.Exceptions;

public sealed class PropertyUnitNoDuplicateException(Guid buildingId, string unitNo)
    : NgbConflictException(
        message: $"Unit number '{unitNo}' already exists in this building.",
        errorCode: "pm.property.unit_no.duplicate",
        context: new Dictionary<string, object?>
        {
            ["buildingId"] = buildingId,
            ["unitNo"] = unitNo
        });
