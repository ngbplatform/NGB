using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentDerivationSourceTypeMismatchException(
    string derivationCode,
    Guid documentId,
    string expectedFromTypeCode,
    string actualFromTypeCode)
    : NgbValidationException(message:
        $"Document derivation '{derivationCode}' expects source type '{expectedFromTypeCode}', but document '{documentId}' is '{actualFromTypeCode}'.",
        errorCode: Code,
        context: new Dictionary<string, object?>
        {
            ["derivationCode"] = derivationCode,
            ["documentId"] = documentId,
            ["expectedFromTypeCode"] = expectedFromTypeCode,
            ["actualFromTypeCode"] = actualFromTypeCode
        })
{
    public const string Code = "doc.derivation.source_type_mismatch";

    public string DerivationCode { get; } = derivationCode;
    public Guid DocumentId { get; } = documentId;
    public string ExpectedFromTypeCode { get; } = expectedFromTypeCode;
    public string ActualFromTypeCode { get; } = actualFromTypeCode;
}
