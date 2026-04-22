using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterSchemaDriftAfterRecordsExistException(
    Guid registerId,
    string table,
    string reason,
    object? details = null)
    : NgbConflictException(
        message: "Reference register records schema drift detected after records exist.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["registerId"] = registerId,
            ["table"] = table,
            ["reason"] = reason,
            ["details"] = details,
        })
{
    public const string Code = "refreg.schema.drift_after_records_exist";

    public Guid RegisterId { get; } = registerId;
    public string Table { get; } = table;
    public string Reason { get; } = reason;
}
