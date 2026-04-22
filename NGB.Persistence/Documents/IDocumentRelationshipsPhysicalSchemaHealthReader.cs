using NGB.Metadata.Documents.Relationships;

namespace NGB.Persistence.Documents;

/// <summary>
/// Reads a health report for the physical schema backing document relationships.
///
/// This is intended for admin/diagnostics endpoints and integration tests.
/// It does not mutate the schema.
/// </summary>
public interface IDocumentRelationshipsPhysicalSchemaHealthReader
{
    Task<DocumentRelationshipsPhysicalSchemaHealth> GetAsync(CancellationToken ct = default);
}
