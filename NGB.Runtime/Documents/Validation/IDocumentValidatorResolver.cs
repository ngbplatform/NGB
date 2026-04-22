using NGB.Definitions;
using NGB.Definitions.Documents.Validation;

namespace NGB.Runtime.Documents.Validation;

/// <summary>
/// Resolves per-document-type validators configured in <see cref="DefinitionsRegistry"/>.
/// </summary>
public interface IDocumentValidatorResolver
{
    IReadOnlyList<IDocumentDraftValidator> ResolveDraftValidators(string typeCode);
    IReadOnlyList<IDocumentPostValidator> ResolvePostValidators(string typeCode);
}
