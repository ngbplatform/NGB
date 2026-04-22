using NGB.Tools.Exceptions;

namespace NGB.Core.Documents.Exceptions;

public sealed class DocumentDerivationNotFoundException(string derivationCode) : NgbConfigurationException(
    message: $"Document derivation '{derivationCode}' was not found. Register it via Definitions.",
    errorCode: Code,
    context: new Dictionary<string, object?>
    {
        ["derivationCode"] = derivationCode
    })
{
    public const string Code = "doc.derivation.not_found";

    public string DerivationCode { get; } = derivationCode;
}
