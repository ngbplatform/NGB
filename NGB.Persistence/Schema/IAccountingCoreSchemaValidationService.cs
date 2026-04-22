using NGB.Tools.Exceptions;

namespace NGB.Persistence.Schema;

/// <summary>
/// Validates the provider schema required by the accounting core (register, turnovers, posting log, closed periods, etc.).
/// Intended for startup diagnostics and integration tests.
/// </summary>
public interface IAccountingCoreSchemaValidationService
{
    /// <summary>
    /// Validates the accounting core schema.
    /// Throws <see cref="NgbConfigurationViolationException"/> on mismatch.
    /// </summary>
    Task ValidateAsync(CancellationToken ct = default);
}
