using NGB.Tools.Exceptions;

namespace NGB.Core.Features;

public sealed class NgbFeatureDisabledException(string feature)
    : NgbNotFoundException(
        "This feature is not enabled for this application.",
        "feature.disabled",
        new Dictionary<string, object?> { ["feature"] = feature });
