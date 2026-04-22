using NGB.Core.Documents.Exceptions;

namespace NGB.Runtime.Documents;

/// <summary>
/// Validates that the database schema matches registered document hybrid metadata.
/// Exposed as an abstraction to keep consumers provider-agnostic and concrete-free.
/// </summary>
public interface IDocumentSchemaValidationService
{
    /// <summary>
    /// Validates all registered document types. Throws <see cref="DocumentSchemaValidationException"/> on mismatch.
    /// </summary>
    Task ValidateAllAsync(CancellationToken ct = default);
}
