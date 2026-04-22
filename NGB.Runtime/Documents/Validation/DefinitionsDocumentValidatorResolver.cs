using NGB.Definitions;
using NGB.Definitions.Documents.Validation;
using NGB.Runtime.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Validation;

internal sealed class DefinitionsDocumentValidatorResolver(
    DefinitionsRegistry definitions,
    IEnumerable<IDocumentDraftValidator> draftValidators,
    IEnumerable<IDocumentPostValidator> postValidators)
    : IDocumentValidatorResolver
{
    private readonly IReadOnlyList<IDocumentDraftValidator> _allDraftValidators = DefinitionRuntimeBindingHelpers.ToReadOnlyList(draftValidators);
    private readonly IReadOnlyList<IDocumentPostValidator> _allPostValidators = DefinitionRuntimeBindingHelpers.ToReadOnlyList(postValidators);
    private readonly Dictionary<string, IReadOnlyList<IDocumentDraftValidator>> _draftValidatorsByTypeCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<IDocumentPostValidator>> _postValidatorsByTypeCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _draftValidatorsGate = new();
    private readonly Lock _postValidatorsGate = new();

    public IReadOnlyList<IDocumentDraftValidator> ResolveDraftValidators(string typeCode)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentRequiredException(nameof(typeCode));

        if (!definitions.TryGetDocument(typeCode, out var def) || def.DraftValidatorTypes.Count == 0)
            return [];

        return ResolveDraftValidators(def);
    }

    public IReadOnlyList<IDocumentPostValidator> ResolvePostValidators(string typeCode)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentRequiredException(nameof(typeCode));

        if (!definitions.TryGetDocument(typeCode, out var def) || def.PostValidatorTypes.Count == 0)
            return [];

        return ResolvePostValidators(def);
    }

    private IReadOnlyList<IDocumentDraftValidator> ResolveDraftValidators(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        if (_draftValidatorsByTypeCode.TryGetValue(def.TypeCode, out var cached))
            return cached;

        lock (_draftValidatorsGate)
        {
            if (_draftValidatorsByTypeCode.TryGetValue(def.TypeCode, out cached))
                return cached;

            var resolved = BuildValidators(def, _allDraftValidators, static x => x.DraftValidatorTypes);
            _draftValidatorsByTypeCode[def.TypeCode] = resolved;
            return resolved;
        }
    }

    private IReadOnlyList<IDocumentPostValidator> ResolvePostValidators(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        if (_postValidatorsByTypeCode.TryGetValue(def.TypeCode, out var cached))
            return cached;

        lock (_postValidatorsGate)
        {
            if (_postValidatorsByTypeCode.TryGetValue(def.TypeCode, out cached))
                return cached;

            var resolved = BuildValidators(def, _allPostValidators, static x => x.PostValidatorTypes);
            _postValidatorsByTypeCode[def.TypeCode] = resolved;
            return resolved;
        }
    }

    private static IReadOnlyList<TValidator> BuildValidators<TValidator>(
        NGB.Definitions.Documents.DocumentTypeDefinition def,
        IReadOnlyList<TValidator> validators,
        Func<NGB.Definitions.Documents.DocumentTypeDefinition, IReadOnlyList<Type>> bindingSelector)
        where TValidator : class
    {
        var boundTypes = bindingSelector(def);
        if (boundTypes.Count == 0)
            return [];

        var resolved = new List<TValidator>(boundTypes.Count);
        foreach (var validatorType in boundTypes)
        {
            if (!typeof(TValidator).IsAssignableFrom(validatorType))
            {
                throw new NgbConfigurationViolationException(
                    $"{typeof(TValidator).Name} '{validatorType.FullName}' must implement {typeof(TValidator).Name} for document type '{def.TypeCode}'.",
                    context: new Dictionary<string, object?>
                    {
                        ["typeCode"] = def.TypeCode,
                        ["validatorType"] = validatorType.FullName,
                        ["validatorContract"] = typeof(TValidator).FullName
                    });
            }

            var matches = DefinitionRuntimeBindingHelpers.FindMatches(validatorType, validators);

            if (matches.Length == 0)
            {
                throw new NgbConfigurationViolationException(
                    $"{typeof(TValidator).Name} '{validatorType.FullName}' is not registered for document type '{def.TypeCode}'.",
                    context: new Dictionary<string, object?>
                    {
                        ["typeCode"] = def.TypeCode,
                        ["validatorType"] = validatorType.FullName,
                        ["validatorContract"] = typeof(TValidator).FullName
                    });
            }

            if (matches.Length > 1)
            {
                throw new NgbConfigurationViolationException(
                    $"{typeof(TValidator).Name} '{validatorType.FullName}' has multiple registrations for document type '{def.TypeCode}'.",
                    context: new Dictionary<string, object?>
                    {
                        ["typeCode"] = def.TypeCode,
                        ["validatorType"] = validatorType.FullName,
                        ["validatorContract"] = typeof(TValidator).FullName,
                        ["matches"] = matches.Select(validator => validator.GetType().FullName ?? validator.GetType().Name).ToArray()
                    });
            }

            var typed = matches[0];
            var actualTypeCode = typed switch
            {
                IDocumentDraftValidator draftValidator => draftValidator.TypeCode,
                IDocumentPostValidator postValidator => postValidator.TypeCode,
                _ => throw new NgbInvariantViolationException(
                    $"Unsupported validator contract '{typeof(TValidator).FullName}'.",
                    new Dictionary<string, object?> { ["validatorContract"] = typeof(TValidator).FullName })
            };

            if (!string.Equals(actualTypeCode, def.TypeCode, StringComparison.Ordinal))
            {
                throw new NgbConfigurationViolationException(
                    $"{typeof(TValidator).Name} '{validatorType.FullName}' TypeCode does not match. Expected '{def.TypeCode}', actual '{actualTypeCode}'.",
                    context: new Dictionary<string, object?>
                    {
                        ["typeCode"] = def.TypeCode,
                        ["validatorType"] = validatorType.FullName,
                        ["expectedTypeCode"] = def.TypeCode,
                        ["actualTypeCode"] = actualTypeCode
                    });
            }

            resolved.Add(typed);
        }

        return resolved;
    }
}
