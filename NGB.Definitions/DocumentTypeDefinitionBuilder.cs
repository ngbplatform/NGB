using NGB.Metadata.Documents.Hybrid;
using NGB.Tools.Exceptions;

namespace NGB.Definitions;

public sealed class DocumentTypeDefinitionBuilder
{
    private readonly DefinitionsBuilder.MutableDocumentTypeDefinition _mutable;

    internal DocumentTypeDefinitionBuilder(DefinitionsBuilder.MutableDocumentTypeDefinition mutable)
        => _mutable = mutable;

    public DocumentTypeDefinitionBuilder Metadata(DocumentTypeMetadata metadata)
    {
        if (metadata is null)
            throw new NgbArgumentRequiredException(nameof(metadata));

        if (_mutable.Metadata is not null)
            throw new NgbConfigurationViolationException(
                $"Document type '{_mutable.TypeCode}' already has metadata configured.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = _mutable.TypeCode,
                    ["field"] = "metadata"
                });

        _mutable.Metadata = metadata;
        return this;
    }

    public DocumentTypeDefinitionBuilder TypedStorage(Type typedStorageType)
    {
        if (typedStorageType is null)
            throw new NgbArgumentRequiredException(nameof(typedStorageType));

        if (_mutable.TypedStorageType is not null)
            throw new NgbConfigurationViolationException(
                $"Document type '{_mutable.TypeCode}' typed storage is already configured.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = _mutable.TypeCode,
                    ["field"] = "typedStorageType"
                });

        _mutable.TypedStorageType = typedStorageType;
        return this;
    }

    public DocumentTypeDefinitionBuilder TypedStorage<TTypedStorage>()
        where TTypedStorage : class
        => TypedStorage(typeof(TTypedStorage));

    public DocumentTypeDefinitionBuilder PostingHandler(Type postingHandlerType)
    {
        if (postingHandlerType is null)
            throw new NgbArgumentRequiredException(nameof(postingHandlerType));

        if (_mutable.PostingHandlerType is not null)
            throw new NgbConfigurationViolationException(
                $"Document type '{_mutable.TypeCode}' already has posting handler configured.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = _mutable.TypeCode,
                    ["field"] = "postingHandlerType"
                });

        _mutable.PostingHandlerType = postingHandlerType;
        return this;
    }

    public DocumentTypeDefinitionBuilder PostingHandler<TPostingHandler>()
        where TPostingHandler : class
        => PostingHandler(typeof(TPostingHandler));

    public DocumentTypeDefinitionBuilder OperationalRegisterPostingHandler(Type postingHandlerType)
    {
        if (postingHandlerType is null)
            throw new NgbArgumentRequiredException(nameof(postingHandlerType));

        if (_mutable.OperationalRegisterPostingHandlerType is not null)
            throw new NgbConfigurationViolationException(
                $"Document type '{_mutable.TypeCode}' already has operational register posting handler configured.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = _mutable.TypeCode,
                    ["field"] = "operationalRegisterPostingHandlerType"
                });

        _mutable.OperationalRegisterPostingHandlerType = postingHandlerType;
        return this;
    }

    public DocumentTypeDefinitionBuilder OperationalRegisterPostingHandler<TPostingHandler>()
        where TPostingHandler : class
        => OperationalRegisterPostingHandler(typeof(TPostingHandler));

    public DocumentTypeDefinitionBuilder ReferenceRegisterPostingHandler(Type postingHandlerType)
    {
        if (postingHandlerType is null)
            throw new NgbArgumentRequiredException(nameof(postingHandlerType));

        if (_mutable.ReferenceRegisterPostingHandlerType is not null)
            throw new NgbConfigurationViolationException(
                $"Document type '{_mutable.TypeCode}' already has reference register posting handler configured.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = _mutable.TypeCode,
                    ["field"] = "referenceRegisterPostingHandlerType"
                });

        _mutable.ReferenceRegisterPostingHandlerType = postingHandlerType;
        return this;
    }

    public DocumentTypeDefinitionBuilder ReferenceRegisterPostingHandler<TPostingHandler>()
        where TPostingHandler : class
        => ReferenceRegisterPostingHandler(typeof(TPostingHandler));

    public DocumentTypeDefinitionBuilder NumberingPolicy(Type numberingPolicyType)
    {
        if (numberingPolicyType is null)
            throw new NgbArgumentRequiredException(nameof(numberingPolicyType));

        if (_mutable.NumberingPolicyType is not null)
            throw new NgbConfigurationViolationException(
                $"Document type '{_mutable.TypeCode}' already has numbering policy configured.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = _mutable.TypeCode,
                    ["field"] = "numberingPolicyType"
                });

        _mutable.NumberingPolicyType = numberingPolicyType;
        return this;
    }

    public DocumentTypeDefinitionBuilder NumberingPolicy<TNumberingPolicy>()
        where TNumberingPolicy : class
        => NumberingPolicy(typeof(TNumberingPolicy));

    public DocumentTypeDefinitionBuilder ApprovalPolicy(Type approvalPolicyType)
    {
        if (approvalPolicyType is null)
            throw new NgbArgumentRequiredException(nameof(approvalPolicyType));

        if (_mutable.ApprovalPolicyType is not null)
            throw new NgbConfigurationViolationException(
                $"Document type '{_mutable.TypeCode}' already has approval policy configured.",
                context: new Dictionary<string, object?>
                {
                    ["definitionKind"] = "document",
                    ["typeCode"] = _mutable.TypeCode,
                    ["field"] = "approvalPolicyType"
                });

        _mutable.ApprovalPolicyType = approvalPolicyType;
        return this;
    }

    public DocumentTypeDefinitionBuilder ApprovalPolicy<TApprovalPolicy>()
        where TApprovalPolicy : class
        => ApprovalPolicy(typeof(TApprovalPolicy));

    public DocumentTypeDefinitionBuilder AddDraftValidator(Type validatorType)
    {
        if (validatorType is null)
            throw new NgbArgumentRequiredException(nameof(validatorType));

        if (!_mutable.DraftValidatorSet.Add(validatorType))
            return this;

        _mutable.DraftValidatorTypes.Add(validatorType);
        return this;
    }

    public DocumentTypeDefinitionBuilder AddDraftValidator<TValidator>()
        where TValidator : class
        => AddDraftValidator(typeof(TValidator));

    public DocumentTypeDefinitionBuilder AddPostValidator(Type validatorType)
    {
        if (validatorType is null)
            throw new NgbArgumentRequiredException(nameof(validatorType));

        if (!_mutable.PostValidatorSet.Add(validatorType))
            return this;

        _mutable.PostValidatorTypes.Add(validatorType);
        return this;
    }

    public DocumentTypeDefinitionBuilder AddPostValidator<TValidator>()
        where TValidator : class
        => AddPostValidator(typeof(TValidator));
}
