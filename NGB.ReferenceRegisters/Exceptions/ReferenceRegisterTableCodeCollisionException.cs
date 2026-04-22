using NGB.Tools.Exceptions;

namespace NGB.ReferenceRegisters.Exceptions;

public sealed class ReferenceRegisterTableCodeCollisionException : NgbConflictException
{
    public const string Code = "refreg.register.table_code_collision";

    public ReferenceRegisterTableCodeCollisionException(
        string code,
        string codeNorm,
        string tableCode,
        Guid collidesWithRegisterId,
        string collidesWithCode,
        string collidesWithCodeNorm)
        : base(
            message: "Reference register table_code collision.",
            errorCode: Code,
            context: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["codeNorm"] = codeNorm,
                ["tableCode"] = tableCode,
                ["collidesWithRegisterId"] = collidesWithRegisterId,
                ["collidesWithCode"] = collidesWithCode,
                ["collidesWithCodeNorm"] = collidesWithCodeNorm,
            })
    {
        CodeValue = code;
        CodeNorm = codeNorm;
        TableCode = tableCode;
        CollidesWithRegisterId = collidesWithRegisterId;
    }

    public ReferenceRegisterTableCodeCollisionException(
        string code,
        string codeNorm,
        string tableCode,
        Exception? innerException)
        : base(
            message: "Reference register table_code collision.",
            errorCode: Code,
            context: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["codeNorm"] = codeNorm,
                ["tableCode"] = tableCode,
                ["collidesWithRegisterId"] = null,
                ["collidesWithCode"] = null,
                ["collidesWithCodeNorm"] = null,
            },
            innerException: innerException)
    {
        CodeValue = code;
        CodeNorm = codeNorm;
        TableCode = tableCode;
    }

    public string CodeValue { get; }
    public string CodeNorm { get; }
    public string TableCode { get; }
    public Guid? CollidesWithRegisterId { get; }
}
