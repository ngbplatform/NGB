using NGB.Metadata.Documents.Hybrid;
using NGB.Tools.Exceptions;

namespace NGB.Definitions.Documents;

/// <summary>
/// Immutable definition of a document type.
/// </summary>
public sealed class DocumentTypeDefinition
{
    public DocumentTypeDefinition(
        string typeCode,
        DocumentTypeMetadata metadata,
        Type? typedStorageType = null,
        Type? postingHandlerType = null,
        Type? operationalRegisterPostingHandlerType = null,
        Type? referenceRegisterPostingHandlerType = null,
        Type? numberingPolicyType = null,
        Type? approvalPolicyType = null,
        IReadOnlyList<Type>? draftValidatorTypes = null,
        IReadOnlyList<Type>? postValidatorTypes = null)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentInvalidException(nameof(typeCode), "Type code must be non-empty.");
        
        TypeCode = typeCode;
        Metadata = metadata ?? throw new NgbArgumentRequiredException(nameof(metadata));

        TypedStorageType = typedStorageType;
        PostingHandlerType = postingHandlerType;
        OperationalRegisterPostingHandlerType = operationalRegisterPostingHandlerType;
        ReferenceRegisterPostingHandlerType = referenceRegisterPostingHandlerType;
        NumberingPolicyType = numberingPolicyType;
        ApprovalPolicyType = approvalPolicyType;
        DraftValidatorTypes = draftValidatorTypes ?? [];
        PostValidatorTypes = postValidatorTypes ?? [];
    }

    public string TypeCode { get; }
    public DocumentTypeMetadata Metadata { get; }

    /// <summary>
    /// Optional type that implements the per-document-type storage (typed tables).
    /// Registered by an industry solution provider module.
    /// </summary>
    public Type? TypedStorageType { get; }

    /// <summary>
    /// Optional type that can build accounting entries for this document.
    /// Registered by a business module.
    /// </summary>
    public Type? PostingHandlerType { get; }

    /// <summary>
    /// Optional type that can build Operational Register movements for this document.
    /// Registered by a business module.
    /// </summary>
    public Type? OperationalRegisterPostingHandlerType { get; }

    /// <summary>
    /// Optional type that can build Reference Register records for this document.
    /// Registered by a business module.
    /// </summary>
    public Type? ReferenceRegisterPostingHandlerType { get; }

    /// <summary>
    /// Optional type that defines numbering policy/formatting rules for this document.
    /// </summary>
    public Type? NumberingPolicyType { get; }

    /// <summary>
    /// Optional type that defines approval workflow rules for this document.
    /// </summary>
    public Type? ApprovalPolicyType { get; }

    /// <summary>
    /// Optional draft validators (executed on create/update/submit).
    /// </summary>
    public IReadOnlyList<Type> DraftValidatorTypes { get; }

    /// <summary>
    /// Optional post validators (executed before posting).
    /// </summary>
    public IReadOnlyList<Type> PostValidatorTypes { get; }
}
