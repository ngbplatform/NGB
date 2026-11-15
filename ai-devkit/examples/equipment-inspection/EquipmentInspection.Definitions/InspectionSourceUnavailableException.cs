using NGB.Tools.Exceptions;

namespace EquipmentInspection.Definitions;

public sealed class InspectionSourceUnavailableException()
    : NgbConflictException(
        "This inspection is no longer available. Select another source.",
        "example.inspection.source_unavailable");
