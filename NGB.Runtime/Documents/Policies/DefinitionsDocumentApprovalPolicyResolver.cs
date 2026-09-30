using NGB.Definitions;
using NGB.Definitions.Documents.Approval;
using NGB.Runtime.Internal;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.Policies;

public sealed class DefinitionsDocumentApprovalPolicyResolver(
    DefinitionsRegistry definitions,
    IEnumerable<IDocumentApprovalPolicy> policies)
    : IDocumentApprovalPolicyResolver
{
    private readonly DefinitionsRegistry _definitions = definitions ?? throw new NgbArgumentRequiredException(nameof(definitions));
    private readonly IReadOnlyList<IDocumentApprovalPolicy> _allPolicies = DefinitionRuntimeBindingHelpers.ToReadOnlyList(
        policies ?? throw new NgbArgumentRequiredException(nameof(policies)));
    private readonly Dictionary<string, IDocumentApprovalPolicy> _policiesByTypeCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _policiesGate = new();

    public IDocumentApprovalPolicy? Resolve(string typeCode)
    {
        if (!_definitions.TryGetDocument(typeCode, out var def))
            return null;

        if (def.ApprovalPolicyType is null)
            return null;

        return ResolvePolicy(def);
    }

    private IDocumentApprovalPolicy ResolvePolicy(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        lock (_policiesGate)
        {
            if (_policiesByTypeCode.TryGetValue(def.TypeCode, out var cached))
                return cached;

            var resolved = BuildPolicy(def);
            _policiesByTypeCode[def.TypeCode] = resolved;
            return resolved;
        }
    }

    private IDocumentApprovalPolicy BuildPolicy(NGB.Definitions.Documents.DocumentTypeDefinition def)
    {
        var policyType = def.ApprovalPolicyType!;

        if (!typeof(IDocumentApprovalPolicy).IsAssignableFrom(policyType))
        {
            throw new DocumentPolicyConfigurationException(
                policyKind: "approval",
                documentTypeCode: def.TypeCode,
                policyType: policyType,
                reason: "Policy type must implement IDocumentApprovalPolicy.");
        }

        var matches = DefinitionRuntimeBindingHelpers.FindMatches(policyType, _allPolicies);

        if (matches.Length == 0)
        {
            throw new DocumentPolicyConfigurationException(
                policyKind: "approval",
                documentTypeCode: def.TypeCode,
                policyType: policyType,
                reason: "Policy type is not registered in the DI container.");
        }

        if (matches.Length > 1)
        {
            throw new DocumentPolicyConfigurationException(
                policyKind: "approval",
                documentTypeCode: def.TypeCode,
                policyType: policyType,
                reason: "Multiple policy registrations match the configured policy type.");
        }

        var resolved = matches[0];
        if (!string.Equals(resolved.TypeCode, def.TypeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DocumentPolicyConfigurationException(
                policyKind: "approval",
                documentTypeCode: def.TypeCode,
                policyType: resolved.GetType(),
                reason: $"Policy TypeCode does not match. Expected '{def.TypeCode}', actual '{resolved.TypeCode}'.");
        }

        return resolved;
    }
}
