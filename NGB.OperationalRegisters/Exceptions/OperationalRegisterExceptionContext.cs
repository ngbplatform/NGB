namespace NGB.OperationalRegisters.Exceptions;

internal static class OperationalRegisterExceptionContext
{
    public static IReadOnlyDictionary<string, object?> Build(
        Guid registerId,
        string reason,
        IReadOnlyDictionary<string, object?>? details = null)
    {
        var ctx = details is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(details);

        ctx["registerId"] = registerId;
        ctx["reason"] = reason;
        return ctx;
    }
}
