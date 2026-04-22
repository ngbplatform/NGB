using NGB.Metadata.Base;

namespace NGB.Persistence.Catalogs.Universal;

/// <summary>
/// Provider-specific writer for universal, metadata-driven catalog CRUD.
///
/// All methods are expected to be called within an active transaction.
/// </summary>
public interface ICatalogWriter
{
    Task UpsertHeadAsync(
        CatalogHeadDescriptor head,
        Guid catalogId,
        IReadOnlyList<CatalogHeadValue> values,
        CancellationToken ct = default);

    Task UpsertHeadsAsync(
        CatalogHeadDescriptor head,
        IReadOnlyList<CatalogHeadWriteRow> rows,
        CancellationToken ct = default);
}

public sealed record CatalogHeadValue(string ColumnName, ColumnType ColumnType, object? Value);

public sealed record CatalogHeadWriteRow(Guid CatalogId, IReadOnlyList<CatalogHeadValue> Values);
