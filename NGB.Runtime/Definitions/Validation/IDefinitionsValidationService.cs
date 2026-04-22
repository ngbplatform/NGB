namespace NGB.Runtime.Definitions.Validation;

/// <summary>
/// Validates that module-contributed Definitions are internally consistent and
/// match the current DI container shape.
/// </summary>
public interface IDefinitionsValidationService
{
    /// <summary>
    /// Validates Definitions and throws <see cref="DefinitionsValidationException"/> on failures.
    /// </summary>
    void ValidateOrThrow();
}
