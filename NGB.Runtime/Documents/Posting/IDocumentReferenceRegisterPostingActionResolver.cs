using NGB.Core.Documents;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.Runtime.Documents.Posting;

/// <summary>
/// Resolves an optional Reference Register records action for a given document.
/// </summary>
public interface IDocumentReferenceRegisterPostingActionResolver
{
    /// <summary>
    /// Returns a delegate that will populate records via <paramref name="builder"/>, or null if the
    /// document type has no Reference Register posting handler configured.
    /// </summary>
    Func<IReferenceRegisterRecordsBuilder, ReferenceRegisterWriteOperation, CancellationToken, Task>? TryResolve(
        DocumentRecord document);
}
