using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterRecordsValidationException(
    Guid registerId,
    string reason,
    object? details = null)
    : NgbValidationException(
        message: "Reference register records validation failed.",
        errorCode: Code,
        context: BuildContext(registerId, reason, details))
{
    public const string Code = "refreg.records.validation_failed";

    public Guid RegisterId { get; } = registerId;
    public string Reason { get; } = reason;

    private static IReadOnlyDictionary<string, object?> BuildContext(Guid registerId, string reason, object? details)
    {
        var ctx = new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["reason"] = reason,
            ["details"] = details,
        };

        // Tests (and callers) often want to assert on individual detail fields without
        // doing deep object inspection (e.g., periodicity/periodUtc/recordMode/etc.).
        // Flatten anonymous-type details into top-level Context keys.
        if (details is null)
            return ctx;

        if (details is IReadOnlyDictionary<string, object?> ro)
        {
            foreach (var (k, v) in ro)
            {
                ctx.TryAdd(k, v);
            }
            
            return ctx;
        }

        if (details is IDictionary<string, object?> dict)
        {
            foreach (var kv in dict)
            {
                ctx.TryAdd(kv.Key, kv.Value);
            }
            
            return ctx;
        }

        var t = details.GetType();
        foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (!p.CanRead)
                continue;

            // ignore indexers
            if (p.GetIndexParameters().Length != 0)
                continue;

            ctx.TryAdd(p.Name, p.GetValue(details));
        }

        return ctx;
    }
}
