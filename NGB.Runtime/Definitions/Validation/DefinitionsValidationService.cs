using Microsoft.Extensions.DependencyInjection;
using NGB.Definitions;
using NGB.Definitions.Catalogs;
using NGB.Definitions.Catalogs.Validation;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Approval;
using NGB.Definitions.Documents.Derivations;
using NGB.Definitions.Documents.Numbering;
using NGB.Definitions.Documents.Posting;
using NGB.Definitions.Documents.Validation;
using NGB.Metadata.Base;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Definitions.Validation;

internal sealed class DefinitionsValidationService(
    DefinitionsRegistry registry,
    IServiceProviderIsService? isService,
    IServiceScopeFactory? scopeFactory)
    : IDefinitionsValidationService
{
    private readonly DefinitionsRegistry _registry = registry ?? throw new NgbArgumentRequiredException(nameof(registry));

    public DefinitionsValidationService(
        DefinitionsRegistry registry,
        IServiceProviderIsService? isService = null)
        : this(registry, isService, scopeFactory: null)
    {
    }

    public void ValidateOrThrow()
    {
        var errors = new List<string>();

        ValidateDocuments(errors);
        ValidateCatalogs(errors);
        ValidateDocumentRelationshipTypes(errors);
        ValidateDocumentDerivations(errors);
        ValidateRuntimeBindings(errors);

        if (errors.Count > 0)
            throw new DefinitionsValidationException(errors);
    }

    private void ValidateRuntimeBindings(List<string> errors)
    {
        if (scopeFactory is null)
            return;

        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var services = scope.ServiceProvider;

            var documentStorages = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<IDocumentTypeStorage>());
            var catalogStorages = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<ICatalogTypeStorage>());
            var postingHandlers = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<IDocumentPostingHandler>());
            var operationalRegisterPostingHandlers = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<IDocumentOperationalRegisterPostingHandler>());
            var referenceRegisterPostingHandlers = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<IDocumentReferenceRegisterPostingHandler>());
            var numberingPolicies = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<IDocumentNumberingPolicy>());
            var approvalPolicies = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<IDocumentApprovalPolicy>());
            var draftValidators = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<IDocumentDraftValidator>());
            var postValidators = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<IDocumentPostValidator>());
            var catalogValidators = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<ICatalogUpsertValidator>());
            var derivationHandlers = DefinitionRuntimeBindingHelpers.ToReadOnlyList(services.GetServices<IDocumentDerivationHandler>());

            foreach (var def in _registry.Documents)
            {
                ValidateResolvedBinding(
                    errors,
                    "Document",
                    def.TypeCode,
                    nameof(DocumentTypeDefinition.TypedStorageType),
                    def.TypedStorageType,
                    typeof(IDocumentTypeStorage),
                    documentStorages,
                    storage => storage.TypeCode,
                    "TypeCode");

                ValidateResolvedBinding(
                    errors,
                    "Document",
                    def.TypeCode,
                    nameof(DocumentTypeDefinition.PostingHandlerType),
                    def.PostingHandlerType,
                    typeof(IDocumentPostingHandler),
                    postingHandlers,
                    handler => handler.TypeCode,
                    "TypeCode");

                ValidateResolvedBinding(
                    errors,
                    "Document",
                    def.TypeCode,
                    nameof(DocumentTypeDefinition.OperationalRegisterPostingHandlerType),
                    def.OperationalRegisterPostingHandlerType,
                    typeof(IDocumentOperationalRegisterPostingHandler),
                    operationalRegisterPostingHandlers,
                    handler => handler.TypeCode,
                    "TypeCode");

                ValidateResolvedBinding(
                    errors,
                    "Document",
                    def.TypeCode,
                    nameof(DocumentTypeDefinition.ReferenceRegisterPostingHandlerType),
                    def.ReferenceRegisterPostingHandlerType,
                    typeof(IDocumentReferenceRegisterPostingHandler),
                    referenceRegisterPostingHandlers,
                    handler => handler.TypeCode,
                    "TypeCode");

                ValidateResolvedBinding(
                    errors,
                    "Document",
                    def.TypeCode,
                    nameof(DocumentTypeDefinition.NumberingPolicyType),
                    def.NumberingPolicyType,
                    typeof(IDocumentNumberingPolicy),
                    numberingPolicies,
                    policy => policy.TypeCode,
                    "TypeCode");

                ValidateResolvedBinding(
                    errors,
                    "Document",
                    def.TypeCode,
                    nameof(DocumentTypeDefinition.ApprovalPolicyType),
                    def.ApprovalPolicyType,
                    typeof(IDocumentApprovalPolicy),
                    approvalPolicies,
                    policy => policy.TypeCode,
                    "TypeCode");

                foreach (var validatorType in def.DraftValidatorTypes)
                {
                    ValidateResolvedBinding(
                        errors,
                        "Document",
                        def.TypeCode,
                        nameof(DocumentTypeDefinition.DraftValidatorTypes),
                        validatorType,
                        typeof(IDocumentDraftValidator),
                        draftValidators,
                        validator => validator.TypeCode,
                        "TypeCode");
                }

                foreach (var validatorType in def.PostValidatorTypes)
                {
                    ValidateResolvedBinding(
                        errors,
                        "Document",
                        def.TypeCode,
                        nameof(DocumentTypeDefinition.PostValidatorTypes),
                        validatorType,
                        typeof(IDocumentPostValidator),
                        postValidators,
                        validator => validator.TypeCode,
                        "TypeCode");
                }
            }

            foreach (var def in _registry.Catalogs)
            {
                ValidateResolvedBinding(
                    errors,
                    "Catalog",
                    def.TypeCode,
                    nameof(CatalogTypeDefinition.TypedStorageType),
                    def.TypedStorageType,
                    typeof(ICatalogTypeStorage),
                    catalogStorages,
                    storage => storage.CatalogCode,
                    "CatalogCode");

                foreach (var validatorType in def.ValidatorTypes)
                {
                    ValidateResolvedBinding(
                        errors,
                        "Catalog",
                        def.TypeCode,
                        nameof(CatalogTypeDefinition.ValidatorTypes),
                        validatorType,
                        typeof(ICatalogUpsertValidator),
                        catalogValidators,
                        validator => validator.TypeCode,
                        "TypeCode");
                }
            }

            foreach (var def in _registry.DocumentDerivations)
            {
                ValidateResolvedBinding(
                    errors,
                    "DocumentDerivation",
                    def.Code,
                    nameof(DocumentDerivationDefinition.HandlerType),
                    def.HandlerType,
                    typeof(IDocumentDerivationHandler),
                    derivationHandlers);
            }
        }
        finally
        {
            scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private void ValidateResolvedBinding<TService>(
        List<string> errors,
        string kind,
        string definitionCode,
        string bindingName,
        Type? candidateType,
        Type requiredInterface,
        IReadOnlyList<TService> registeredServices,
        Func<TService, string>? codeAccessor = null,
        string codePropertyName = "TypeCode")
        where TService : class
    {
        if (!CanValidateResolvedBinding(candidateType, requiredInterface))
            return;

        if (isService is not null && !isService.IsService(candidateType!))
            return;

        var matches = DefinitionRuntimeBindingHelpers.FindMatches(candidateType!, registeredServices);

        if (matches.Length == 0)
        {
            errors.Add($"{kind} '{definitionCode}': {bindingName} '{candidateType!.FullName}' is not registered in DI as {requiredInterface.Name}.");
            return;
        }

        if (matches.Length > 1)
        {
            errors.Add(
                $"{kind} '{definitionCode}': {bindingName} '{candidateType!.FullName}' has multiple matching DI registrations for {requiredInterface.Name}. " +
                $"count={matches.Length}.");
            return;
        }

        if (codeAccessor is null)
            return;

        var actualCode = codeAccessor(matches[0]);

        if (string.Equals(actualCode, definitionCode, StringComparison.OrdinalIgnoreCase))
            return;

        errors.Add(
            $"{kind} '{definitionCode}': {bindingName} '{candidateType!.FullName}' resolved {codePropertyName} '{actualCode}' does not match definition code '{definitionCode}'.");
    }

    private static bool CanValidateResolvedBinding(Type? candidateType, Type requiredInterface)
    {
        if (candidateType is null)
            return false;

        if (candidateType.IsInterface || candidateType.IsAbstract)
            return false;

        if (candidateType.ContainsGenericParameters)
            return false;

        return requiredInterface.IsAssignableFrom(candidateType);
    }

    private void ValidateDocumentDerivations(List<string> errors)
    {
        foreach (var def in _registry.DocumentDerivations)
        {
            if (string.IsNullOrWhiteSpace(def.Code) || !string.Equals(def.Code, def.Code.Trim(), StringComparison.Ordinal))
                errors.Add("DocumentDerivation: Code must be a non-empty trimmed string.");

            if (def.Code.Length > 128)
                errors.Add($"DocumentDerivation '{def.Code}': Code exceeds max length 128.");

            if (string.IsNullOrWhiteSpace(def.Name))
                errors.Add($"DocumentDerivation '{def.Code}': Name must be non-empty.");

            if (string.IsNullOrWhiteSpace(def.FromTypeCode))
                errors.Add($"DocumentDerivation '{def.Code}': FromTypeCode must be non-empty.");
            else if (!_registry.TryGetDocument(def.FromTypeCode, out _))
                errors.Add($"DocumentDerivation '{def.Code}': FromTypeCode references unknown document type '{def.FromTypeCode}'.");

            if (string.IsNullOrWhiteSpace(def.ToTypeCode))
                errors.Add($"DocumentDerivation '{def.Code}': ToTypeCode must be non-empty.");
            else if (!_registry.TryGetDocument(def.ToTypeCode, out _))
                errors.Add($"DocumentDerivation '{def.Code}': ToTypeCode references unknown document type '{def.ToTypeCode}'.");

            if (def.RelationshipCodes is null || def.RelationshipCodes.Count == 0)
            {
                errors.Add($"DocumentDerivation '{def.Code}': RelationshipCodes must contain at least one code.");
            }
            else
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var relCode in def.RelationshipCodes)
                {
                    if (string.IsNullOrWhiteSpace(relCode))
                    {
                        errors.Add($"DocumentDerivation '{def.Code}': RelationshipCodes contains an empty code.");
                        continue;
                    }

                    var trimmed = relCode.Trim();

                    if (!string.Equals(relCode, trimmed, StringComparison.Ordinal))
                        errors.Add($"DocumentDerivation '{def.Code}': RelationshipCodes contains a non-trimmed code '{relCode}'.");

                    if (trimmed.Length > 128)
                        errors.Add($"DocumentDerivation '{def.Code}': RelationshipCodes contains a code exceeding max length 128. code='{trimmed}'.");

                    if (!seen.Add(trimmed))
                        errors.Add($"DocumentDerivation '{def.Code}': RelationshipCodes contains duplicate code '{trimmed}'.");

                    if (!_registry.TryGetDocumentRelationshipType(trimmed, out _))
                        errors.Add($"DocumentDerivation '{def.Code}': RelationshipCodes references unknown relationship type '{trimmed}'.");
                }
            }

            if (def.HandlerType is not null)
                ValidateBinding(errors, "DocumentDerivation", def.Code, "HandlerType", def.HandlerType, typeof(IDocumentDerivationHandler));
        }
    }

    private void ValidateDocumentRelationshipTypes(List<string> errors)
    {
        foreach (var def in _registry.DocumentRelationshipTypes)
        {
            if (string.IsNullOrWhiteSpace(def.Code) || !string.Equals(def.Code, def.Code.Trim(), StringComparison.Ordinal))
                errors.Add("DocumentRelationshipType: Code must be a non-empty trimmed string.");

            if (def.Code.Length > 128)
                errors.Add($"DocumentRelationshipType '{def.Code}': Code exceeds max length 128.");

            if (string.IsNullOrWhiteSpace(def.Name))
                errors.Add($"DocumentRelationshipType '{def.Code}': Name must be non-empty.");

            ValidateAllowedDocumentTypeCodes(errors, def.Code, "AllowedFromTypeCodes", def.AllowedFromTypeCodes);
            ValidateAllowedDocumentTypeCodes(errors, def.Code, "AllowedToTypeCodes", def.AllowedToTypeCodes);

            if (def is { IsBidirectional: true, AllowedFromTypeCodes: not null, AllowedToTypeCodes: not null })
            {
                var from = new HashSet<string>(def.AllowedFromTypeCodes, StringComparer.OrdinalIgnoreCase);
                var to = new HashSet<string>(def.AllowedToTypeCodes, StringComparer.OrdinalIgnoreCase);

                if (!from.SetEquals(to))
                    errors.Add($"DocumentRelationshipType '{def.Code}': Bidirectional relationship must have identical AllowedFromTypeCodes and AllowedToTypeCodes (or omit both).");
            }
        }
    }

    private void ValidateAllowedDocumentTypeCodes(
        List<string> errors,
        string relationshipCode,
        string propertyName,
        IReadOnlyCollection<string>? codes)
    {
        if (codes is null)
            return;

        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                errors.Add($"DocumentRelationshipType '{relationshipCode}': {propertyName} contains an empty TypeCode.");
                continue;
            }

            if (!_registry.TryGetDocument(code, out _))
                errors.Add($"DocumentRelationshipType '{relationshipCode}': {propertyName} references unknown document type '{code}'.");
        }
    }

    private void ValidateDocuments(List<string> errors)
    {
        foreach (var def in _registry.Documents)
        {
            if (!string.Equals(def.Metadata.TypeCode, def.TypeCode, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Document '{def.TypeCode}': Metadata.TypeCode does not match. Expected '{def.TypeCode}', actual '{def.Metadata.TypeCode}'.");

            ValidateBinding(
                errors,
                "Document",
                def.TypeCode,
                nameof(DocumentTypeDefinition.TypedStorageType),
                def.TypedStorageType,
                typeof(IDocumentTypeStorage));

            ValidateBinding(
                errors,
                "Document",
                def.TypeCode,
                nameof(DocumentTypeDefinition.PostingHandlerType),
                def.PostingHandlerType,
                typeof(IDocumentPostingHandler));

            ValidateBinding(
                errors,
                "Document",
                def.TypeCode,
                nameof(DocumentTypeDefinition.OperationalRegisterPostingHandlerType),
                def.OperationalRegisterPostingHandlerType,
                typeof(IDocumentOperationalRegisterPostingHandler));

            ValidateBinding(
                errors,
                "Document",
                def.TypeCode,
                nameof(DocumentTypeDefinition.ReferenceRegisterPostingHandlerType),
                def.ReferenceRegisterPostingHandlerType,
                typeof(IDocumentReferenceRegisterPostingHandler));

            ValidateBinding(
                errors,
                "Document",
                def.TypeCode,
                nameof(DocumentTypeDefinition.NumberingPolicyType),
                def.NumberingPolicyType,
                typeof(IDocumentNumberingPolicy));

            ValidateBinding(
                errors,
                "Document",
                def.TypeCode,
                nameof(DocumentTypeDefinition.ApprovalPolicyType),
                def.ApprovalPolicyType,
                typeof(IDocumentApprovalPolicy));

            foreach (var t in def.DraftValidatorTypes)
            {
                ValidateBinding(
                    errors,
                    "Document",
                    def.TypeCode,
                    $"{nameof(DocumentTypeDefinition.DraftValidatorTypes)}",
                    t,
                    typeof(IDocumentDraftValidator));
            }

            foreach (var t in def.PostValidatorTypes)
            {
                ValidateBinding(
                    errors,
                    "Document",
                    def.TypeCode,
                    $"{nameof(DocumentTypeDefinition.PostValidatorTypes)}",
                    t,
                    typeof(IDocumentPostValidator));
            }

            ValidateDocumentAmountField(errors, def);
            ValidateDocumentPartCodes(errors, def);
            ValidateMirroredRelationships(errors, def);
        }
    }

    private static void ValidateDocumentAmountField(List<string> errors, DocumentTypeDefinition def)
    {
        var amountField = def.Metadata.Presentation?.AmountField;
        if (amountField is null)
            return;

        var location = $"Document '{def.TypeCode}': Presentation.AmountField";

        if (string.IsNullOrWhiteSpace(amountField)
            || !string.Equals(amountField, amountField.Trim(), StringComparison.Ordinal))
        {
            errors.Add($"{location} must be a non-empty trimmed head-field name.");
            return;
        }

        var headTable = def.Metadata.Tables.FirstOrDefault(x => x.Kind == TableKind.Head);
        if (headTable is null)
        {
            errors.Add($"{location} cannot be validated because the document has no head table metadata.");
            return;
        }

        var column = headTable.Columns.FirstOrDefault(x => string.Equals(x.ColumnName, amountField, StringComparison.OrdinalIgnoreCase));
        if (column is null)
        {
            errors.Add($"{location} '{amountField}' must reference an existing numeric head-table column.");
            return;
        }

        if (column.Type is not (ColumnType.Decimal or ColumnType.Int32 or ColumnType.Int64))
        {
            errors.Add($"{location} '{amountField}' must reference a Decimal, Int32, or Int64 head-table column.");
        }
    }

    private void ValidateMirroredRelationships(List<string> errors, DocumentTypeDefinition def)
    {
        foreach (var table in def.Metadata.Tables)
        {
            foreach (var column in table.Columns.Where(x => x.MirroredRelationship is not null))
            {
                var mirrored = column.MirroredRelationship!;
                var location = $"Document '{def.TypeCode}': Mirrored relationship field '{table.TableName}.{column.ColumnName}'";

                if (table.Kind != TableKind.Head)
                    errors.Add($"{location} must be declared on a head-table column.");

                if (IsDocumentId(column.ColumnName))
                    errors.Add($"{location} cannot target system column 'document_id'.");

                if (column.Type != ColumnType.Guid)
                    errors.Add($"{location} must use ColumnType.Guid.");

                if (string.IsNullOrWhiteSpace(mirrored.RelationshipCode)
                    || !string.Equals(mirrored.RelationshipCode, mirrored.RelationshipCode.Trim(), StringComparison.Ordinal))
                {
                    errors.Add($"{location} must specify a non-empty trimmed relationship code.");
                    continue;
                }

                if (column.Lookup is not DocumentLookupSourceMetadata documentLookup)
                {
                    errors.Add($"{location} must be a document lookup field.");
                    continue;
                }

                if (!_registry.TryGetDocumentRelationshipType(mirrored.RelationshipCode, out var relationshipType))
                {
                    errors.Add($"{location} references unknown relationship type '{mirrored.RelationshipCode}'.");
                    continue;
                }

                if (relationshipType.AllowedFromTypeCodes is not null
                    && !relationshipType.AllowedFromTypeCodes.Contains(def.TypeCode, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"{location} uses relationship '{relationshipType.Code}' which does not allow from-document type '{def.TypeCode}'.");
                }

                foreach (var targetType in documentLookup.DocumentTypes)
                {
                    if (!_registry.TryGetDocument(targetType, out _))
                    {
                        errors.Add($"{location} references unknown target document type '{targetType}'.");
                        continue;
                    }

                    if (relationshipType.AllowedToTypeCodes is not null
                        && !relationshipType.AllowedToTypeCodes.Contains(targetType, StringComparer.OrdinalIgnoreCase))
                    {
                        errors.Add($"{location} uses relationship '{relationshipType.Code}' which does not allow target document type '{targetType}'.");
                    }
                }
            }
        }
    }

    private void ValidateCatalogs(List<string> errors)
    {
        foreach (var def in _registry.Catalogs)
        {
            if (!string.Equals(def.Metadata.CatalogCode, def.TypeCode, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Catalog '{def.TypeCode}': Metadata.CatalogCode does not match. Expected '{def.TypeCode}', actual '{def.Metadata.CatalogCode}'.");

            ValidateBinding(errors, "Catalog", def.TypeCode, nameof(CatalogTypeDefinition.TypedStorageType), def.TypedStorageType, typeof(ICatalogTypeStorage));
            ValidateCatalogPartCodes(errors, def);

            foreach (var t in def.ValidatorTypes)
            {
                ValidateBinding(
                    errors,
                    "Catalog",
                    def.TypeCode,
                    $"{nameof(CatalogTypeDefinition.ValidatorTypes)}",
                    t,
                    typeof(ICatalogUpsertValidator));
            }
        }
    }

    private static void ValidateDocumentPartCodes(List<string> errors, DocumentTypeDefinition def)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in def.Metadata.Tables)
        {
            var location = $"Document '{def.TypeCode}' table '{table.TableName}'";

            if (table.Kind != TableKind.Part)
            {
                if (!string.IsNullOrWhiteSpace(table.PartCode))
                    errors.Add($"{location} is not a part table and cannot declare PartCode.");

                continue;
            }

            if (string.IsNullOrWhiteSpace(table.PartCode))
            {
                errors.Add($"{location} must declare a non-empty PartCode.");
                continue;
            }

            if (!string.Equals(table.PartCode, table.PartCode.Trim(), StringComparison.Ordinal))
            {
                errors.Add($"{location} must declare a trimmed PartCode.");
                continue;
            }

            if (!seen.Add(table.PartCode))
                errors.Add($"{location} declares duplicate PartCode '{table.PartCode}'.");
        }
    }

    private static void ValidateCatalogPartCodes(List<string> errors, CatalogTypeDefinition def)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in def.Metadata.Tables)
        {
            var location = $"Catalog '{def.TypeCode}' table '{table.TableName}'";

            if (table.Kind != TableKind.Part)
            {
                if (!string.IsNullOrWhiteSpace(table.PartCode))
                    errors.Add($"{location} is not a part table and cannot declare PartCode.");

                continue;
            }

            if (string.IsNullOrWhiteSpace(table.PartCode))
            {
                errors.Add($"{location} must declare a non-empty PartCode.");
                continue;
            }

            if (!string.Equals(table.PartCode, table.PartCode.Trim(), StringComparison.Ordinal))
            {
                errors.Add($"{location} must declare a trimmed PartCode.");
                continue;
            }

            if (!seen.Add(table.PartCode))
                errors.Add($"{location} declares duplicate PartCode '{table.PartCode}'.");
        }
    }

    private void ValidateBinding(
        List<string> errors,
        string kind,
        string typeCode,
        string bindingName,
        Type? candidateType,
        Type requiredInterface)
    {
        if (candidateType is null)
            return;

        if (candidateType.IsInterface || candidateType.IsAbstract)
        {
            errors.Add($"{kind} '{typeCode}': {bindingName} must be a concrete type. type={candidateType.FullName}");
            return;
        }

        if (candidateType.ContainsGenericParameters)
        {
            errors.Add($"{kind} '{typeCode}': {bindingName} must be a closed constructed type (no open generics). type={candidateType.FullName}");
            return;
        }

        if (!requiredInterface.IsAssignableFrom(candidateType))
        {
            errors.Add($"{kind} '{typeCode}': {bindingName} must implement {requiredInterface.Name}. type={candidateType.FullName}");
            return;
        }

        if (isService is null)
        {
            errors.Add($"{kind} '{typeCode}': cannot validate DI registration for {bindingName} because IServiceProviderIsService is not available. type={candidateType.FullName}");
            return;
        }

        // IMPORTANT:
        // Runtime resolvers resolve bindings by the concrete Type declared in the Definition,
        // so the module must register the concrete type itself in DI.
        if (!isService.IsService(candidateType))
        {
            errors.Add($"{kind} '{typeCode}': {bindingName} '{candidateType.FullName}' is not registered in DI.");
        }
    }

    private static bool IsDocumentId(string name)
        => string.Equals(name, "document_id", StringComparison.OrdinalIgnoreCase);
}
