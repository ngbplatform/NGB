using NGB.Tools.Exceptions;

namespace NGB.OperationalRegisters.Exceptions;

public sealed class OperationalRegisterTableCodeCollisionException(
    string code,
    string codeNorm,
    string tableCode,
    Guid collidesWithRegisterId,
    string collidesWithCode,
    string collidesWithCodeNorm)
    : NgbConflictException(
        message: "Operational register table name collision (table_code).",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["code"] = code,
            ["codeNorm"] = codeNorm,
            ["tableCode"] = tableCode,
            ["collidesWithRegisterId"] = collidesWithRegisterId,
            ["collidesWithCode"] = collidesWithCode,
            ["collidesWithCodeNorm"] = collidesWithCodeNorm
        })
{
    public const string Code = "opreg.register.table_code_collision";

    public string CodeValue { get; } = code;
    public string CodeNorm { get; } = codeNorm;
    public string TableCode { get; } = tableCode;
    public Guid CollidesWithRegisterId { get; } = collidesWithRegisterId;
}
