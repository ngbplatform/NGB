using Microsoft.Extensions.Logging;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Schema;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.Schema;
using NGB.Runtime.Catalogs.Schema;
using NGB.Runtime.Diagnostics;

namespace NGB.Runtime.Catalogs;

internal sealed class CatalogSchemaValidationService(
    ICatalogTypeRegistry registry,
    IDbSchemaInspector schema,
    IDbTypeMapper typeMapper,
    ICatalogTypeStorageResolver storages,
    ILogger<CatalogSchemaValidationService> logger)
    : ICatalogSchemaValidationService
{
    public async Task<SchemaDiagnosticsResult> DiagnoseAllAsync(CancellationToken ct = default)
    {
        RuntimeLog.SchemaValidationStarted(logger, "Catalogs");

        var result = new SchemaDiagnosticsResult();
        var snapshot = await schema.GetSnapshotAsync(ct);

        foreach (var meta in registry.All())
        {
            // Platform contract: if a catalog declares per-type tables in metadata,
            // a module must provide its per-type storage implementation.
            if (meta.Tables.Count > 0 && storages.TryResolve(meta.CatalogCode) is null)
                result.AddError($"Catalog '{meta.CatalogCode}': missing ICatalogTypeStorage registration.");

            ValidateCatalogType(meta, snapshot, result);
        }

        RuntimeLog.SchemaValidationCompleted(logger, "Catalogs");
        return result;
    }

    public async Task ValidateAllAsync(CancellationToken ct = default)
    {
        var diag = await DiagnoseAllAsync(ct);
        if (diag.HasErrors)
            throw new CatalogSchemaValidationException(diag.ToSingleLineString());
    }

    private void ValidateCatalogType(CatalogTypeMetadata meta, DbSchemaSnapshot snapshot, SchemaDiagnosticsResult result)
    {
        foreach (var table in meta.Tables)
        {
            if (!snapshot.Tables.Contains(table.TableName))
            {
                result.AddError($"Catalog '{meta.CatalogCode}': table '{table.TableName}' does not exist.");
                continue;
            }

            if (!snapshot.ColumnsByTable.TryGetValue(table.TableName, out var columnList))
            {
                result.AddError($"Catalog '{meta.CatalogCode}': cannot read columns for table '{table.TableName}'.");
                continue;
            }

            var columns = columnList
                .GroupBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Required hybrid contract: catalog_id must exist and be NOT NULL
            if (!columns.TryGetValue("catalog_id", out var catalogIdCol))
            {
                result.AddError($"Catalog '{meta.CatalogCode}': table '{table.TableName}' must have column 'catalog_id'.");
            }
            else if (catalogIdCol.IsNullable)
            {
                result.AddError($"Catalog '{meta.CatalogCode}': table '{table.TableName}'.catalog_id must be NOT NULL.");
            }

            // FK catalog_id -> catalogs(id)
            snapshot.ForeignKeysByTable.TryGetValue(table.TableName, out var fks);
            fks ??= [];

            var hasFk = fks.Any(fk =>
                string.Equals(fk.ColumnName, "catalog_id", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.ReferencedTableName, "catalogs", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fk.ReferencedColumnName, "id", StringComparison.OrdinalIgnoreCase));

            if (!hasFk)
                result.AddError($"Catalog '{meta.CatalogCode}': table '{table.TableName}' must have FK catalog_id -> catalogs(id).");

            foreach (var c in table.Columns)
            {
                if (!columns.TryGetValue(c.ColumnName, out var actual))
                {
                    result.AddError($"Catalog '{meta.CatalogCode}': table '{table.TableName}' missing column '{c.ColumnName}'.");
                    continue;
                }

                if (c.Required && actual.IsNullable)
                    result.AddError($"Catalog '{meta.CatalogCode}': table '{table.TableName}'.{c.ColumnName} must be NOT NULL.");

                if (!typeMapper.IsCompatible(c.ColumnType, actual.DbType))
                {
                    var expected = typeMapper.GetExpectedDbType(c.ColumnType);
                    result.AddError($"Catalog '{meta.CatalogCode}': table '{table.TableName}'.{c.ColumnName} has type '{actual.DbType}', expected compatible with '{expected}'.");
                }

                if (c.MaxLength is not null
                    && actual.CharacterMaximumLength is not null
                    && actual.CharacterMaximumLength.Value < c.MaxLength.Value)
                {
                    result.AddError($"Catalog '{meta.CatalogCode}': table '{table.TableName}'.{c.ColumnName} max length is {actual.CharacterMaximumLength}, expected >= {c.MaxLength}.");
                }
            }

            // Index checks (best-effort; warnings only)
            if (table.Indexes.Count > 0)
            {
                snapshot.IndexesByTable.TryGetValue(table.TableName, out var actualIndexList);
                actualIndexList ??= [];

                var byName = actualIndexList
                    .GroupBy(i => i.IndexName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var ix in table.Indexes)
                {
                    if (!byName.TryGetValue(ix.Name, out var actualIx))
                    {
                        result.AddWarning($"Catalog '{meta.CatalogCode}': table '{table.TableName}' missing index '{ix.Name}'.");
                        continue;
                    }

                    if (ix.Unique && !actualIx.IsUnique)
                        result.AddWarning($"Catalog '{meta.CatalogCode}': table '{table.TableName}' index '{ix.Name}' should be UNIQUE.");

                    if (ix.ColumnNames.Count > 0 && !ColumnsEqual(ix.ColumnNames.ToArray(), actualIx.ColumnNames.ToArray()))
                    {
                        result.AddWarning(
                            $"Catalog '{meta.CatalogCode}': table '{table.TableName}' index '{ix.Name}' columns mismatch. " +
                            $"Expected [{string.Join(", ", ix.ColumnNames)}], got [{string.Join(", ", actualIx.ColumnNames)}].");
                    }
                }
            }
        }
    }

    private static bool ColumnsEqual(string[] expected, string[] actual)
    {
        if (expected.Length != actual.Length)
            return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (!expected[i].Equals(actual[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
