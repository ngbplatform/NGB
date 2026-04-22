using NGB.Runtime.Catalogs.Schema;

namespace NGB.Runtime.Catalogs;

/// <summary>
/// Validates that the database schema matches registered catalog hybrid metadata.
/// Exposed as an abstraction to keep consumers provider-agnostic and concrete-free.
/// </summary>
public interface ICatalogSchemaValidationService
{
    Task<SchemaDiagnosticsResult> DiagnoseAllAsync(CancellationToken ct = default);
    
    Task ValidateAllAsync(CancellationToken ct = default);
}
