using NGB.Tools.Exceptions;

namespace NGB.Persistence.Schema;

/// <summary>
/// Validates the provider schema required by the Documents core (documents subsystem).
/// Intended for startup diagnostics and integration tests.
/// </summary>
public interface IDocumentsCoreSchemaValidationService
{
    /// <summary>
    /// Validates the Documents core schema.
    /// Throws <see cref="NgbConfigurationViolationException"/> on mismatch.
    /// </summary>
    Task ValidateAsync(CancellationToken ct = default);
}
