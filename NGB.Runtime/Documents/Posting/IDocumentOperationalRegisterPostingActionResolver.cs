using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.Documents.Posting;

/// <summary>
/// Resolves an optional Operational Register movements action for a given document.
/// </summary>
public interface IDocumentOperationalRegisterPostingActionResolver
{
    /// <summary>
    /// Returns a delegate that will populate movements via <paramref name="builder"/>, or null if the
    /// document type has no Operational Register posting handler configured.
    /// </summary>
    Func<IOperationalRegisterMovementsBuilder, CancellationToken, Task>? TryResolve(DocumentRecord document);
}
