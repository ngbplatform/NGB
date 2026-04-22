using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.Runtime.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Policies;

public sealed class DefinitionsDocumentNumberingPolicyResolver(
    DefinitionsRegistry definitions,
    IEnumerable<IDocumentNumberingPolicy> policies) : IDocumentNumberingPolicyResolver
{
    private readonly IReadOnlyList<IDocumentNumberingPolicy> _allPolicies = DefinitionRuntimeBindingHelpers.ToReadOnlyList(policies);
    private readonly Dictionary<string, IDocumentNumberingPolicy> _policiesByTypeCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _policiesGate = new();

    public IDocumentNumberingPolicy? Resolve(string typeCode)
    {
        if (!definitions.TryGetDocument(typeCode, out var def))
            return null;

        if (def.NumberingPolicyType is null)
            return null;

        return ResolvePolicy(def);
    }

    private IDocumentNumberingPolicy ResolvePolicy(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        if (_policiesByTypeCode.TryGetValue(def.TypeCode, out var cached))
            return cached;

        lock (_policiesGate)
        {
            if (_policiesByTypeCode.TryGetValue(def.TypeCode, out cached))
                return cached;

            var resolved = BuildPolicy(def);
            _policiesByTypeCode[def.TypeCode] = resolved;
            return resolved;
        }
    }

    private IDocumentNumberingPolicy BuildPolicy(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        var policyType = def.NumberingPolicyType
            ?? throw new NgbConfigurationViolationException($"Document '{def.TypeCode}' has no numbering policy binding.");

        if (!typeof(IDocumentNumberingPolicy).IsAssignableFrom(policyType))
        {
            throw new DocumentPolicyConfigurationException(
                policyKind: "numbering",
                documentTypeCode: def.TypeCode,
                policyType: policyType,
                reason: "Policy type must implement IDocumentNumberingPolicy.");
        }

        var matches = DefinitionRuntimeBindingHelpers.FindMatches(policyType, _allPolicies);

        if (matches.Length == 0)
        {
            throw new DocumentPolicyConfigurationException(
                policyKind: "numbering",
                documentTypeCode: def.TypeCode,
                policyType: policyType,
                reason: "Policy type is not registered in the DI container.");
        }

        if (matches.Length > 1)
        {
            throw new DocumentPolicyConfigurationException(
                policyKind: "numbering",
                documentTypeCode: def.TypeCode,
                policyType: policyType,
                reason: "Multiple policy registrations match the configured policy type.");
        }

        var resolved = matches[0];
        if (!string.Equals(resolved.TypeCode, def.TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentPolicyConfigurationException(
                policyKind: "numbering",
                documentTypeCode: def.TypeCode,
                policyType: resolved.GetType(),
                reason: $"Policy TypeCode does not match. Expected '{def.TypeCode}', actual '{resolved.TypeCode}'.");
        }

        return resolved;
    }
}
