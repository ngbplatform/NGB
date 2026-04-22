using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentTypeMismatchException(
    Guid documentId,
    string expectedTypeCode,
    string actualTypeCode)
    : NgbValidationException(
        message: $"Document type mismatch. Expected '{expectedTypeCode}', actual '{actualTypeCode}'.",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>
        {
            ["documentId"] = documentId,
            ["expectedTypeCode"] = expectedTypeCode,
            ["actualTypeCode"] = actualTypeCode,
        })
{
    public const string ErrorCodeConst = "doc.type_mismatch";

    public Guid DocumentId { get; } = documentId;

    public string ExpectedTypeCode { get; } = expectedTypeCode;

    public string ActualTypeCode { get; } = actualTypeCode;
}
