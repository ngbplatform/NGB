namespace NGB.Definitions.Documents.Derivations;

/// <summary>
/// Declarative configuration for the platform feature "Create based on" (aka "Enter based on").
///
/// A derivation is a named action available for a given source document type that creates a new
/// draft document of the target type, optionally prefilling it via a handler, and then links the
/// derived document to the source document using one or more relationship types.
///
/// NOTES
/// - Platform-only: there is no Web/API dependency.
/// - Relationship edges are always created as outgoing links from the derived draft:
///   derived -> source.
/// - The optional <see cref="HandlerType"/> can perform domain-specific prefilling (copy fields,
///   build lines, etc.) inside the same transaction.
/// </summary>
public sealed record DocumentDerivationDefinition(
    string Code,
    string Name,
    string FromTypeCode,
    string ToTypeCode,
    IReadOnlyList<string> RelationshipCodes,
    Type? HandlerType);
