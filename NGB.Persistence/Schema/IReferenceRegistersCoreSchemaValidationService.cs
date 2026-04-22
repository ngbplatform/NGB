using NGB.Tools.Exceptions;

namespace NGB.Persistence.Schema;

/// <summary>
/// Validates the provider schema required by Reference Registers (metadata tables + core invariants).
/// Intended for startup diagnostics and integration tests.
/// </summary>
public interface IReferenceRegistersCoreSchemaValidationService
{
    /// <summary>
    /// Validates the Reference Registers core schema.
    /// Throws <see cref="NgbConfigurationViolationException"/> on mismatch.
    /// </summary>
    Task ValidateAsync(CancellationToken ct = default);
}
