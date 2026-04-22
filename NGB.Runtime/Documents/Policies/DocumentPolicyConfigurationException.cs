using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Policies;

/// <summary>
/// Thrown when a document policy (approval/numbering) is misconfigured in Definitions/DI.
/// </summary>
public sealed class DocumentPolicyConfigurationException(
    string policyKind,
    string documentTypeCode,
    Type policyType,
    string reason,
    Exception? innerException = null)
    : NgbConfigurationException(
        message: $"Invalid {policyKind} policy configuration for document type '{documentTypeCode}': {reason}",
        errorCode: ErrorCodeConst,
        context: new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["policyKind"] = policyKind,
            ["documentTypeCode"] = documentTypeCode,
            ["policyType"] = policyType.FullName,
            ["reason"] = reason
        },
        innerException: innerException)
{
    public const string ErrorCodeConst = "documents.policy.configuration_error";

    public string PolicyKind { get; } = policyKind;
    public string DocumentTypeCode { get; } = documentTypeCode;
    public Type PolicyType { get; } = policyType;
    public string Reason { get; } = reason;
}
