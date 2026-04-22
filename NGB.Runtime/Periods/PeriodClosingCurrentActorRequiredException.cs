using NGB.Tools.Exceptions;

namespace NGB.Runtime.Periods;

public sealed class PeriodClosingCurrentActorRequiredException()
    : NgbForbiddenException(
        message: "An authenticated current user is required to perform period closing operations.",
        errorCode: "period.actor.required");
