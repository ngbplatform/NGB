using NGB.Accounting.Posting;
using NGB.Core.Documents;

namespace NGB.Runtime.Documents.Posting;

/// <summary>
/// Resolves an <b>accounting</b> posting action (delegate) for a given document based on its definition.
///
/// Accounting posting is optional at the platform level: some documents post only to registers
/// (Operational / Reference) and therefore have no accounting posting handler configured.
/// </summary>
public interface IDocumentPostingActionResolver
{
    /// <summary>
    /// Tries to resolve the accounting posting delegate. Returns <c>null</c> when the document type
    /// has no accounting posting handler configured.
    /// </summary>
    Func<IAccountingPostingContext, CancellationToken, Task>? TryResolve(DocumentRecord document);

    /// <summary>
    /// Resolves the accounting posting delegate.
    /// Throws when the document type has no accounting posting handler configured.
    /// </summary>
    Func<IAccountingPostingContext, CancellationToken, Task> Resolve(DocumentRecord document);
}
