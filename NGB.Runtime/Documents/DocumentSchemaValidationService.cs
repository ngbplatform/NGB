using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NGB.Core.Documents.Exceptions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Metadata.Schema;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.Schema;

namespace NGB.Runtime.Documents;

/// <summary>
/// Validates that the database schema matches registered document hybrid metadata.
/// Provider-agnostic: relies on <see cref="IDbSchemaInspector"/> and <see cref="IDbTypeMapper"/>.
/// Intended to run at startup / during diagnostics.
/// </summary>
internal sealed class DocumentSchemaValidationService(
    IDocumentTypeRegistry registry,
    IDbSchemaInspector schema,
    IDbTypeMapper typeMapper,
    ILogger<DocumentSchemaValidationService> logger)
    : IDocumentSchemaValidationService
{
    /// <summary>
    /// Validates all registered document types. Throws <see cref="DocumentSchemaValidationException"/> on mismatch.
    /// </summary>
    public async Task ValidateAllAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var docTypes = registry.GetAll().ToList();
        logger.LogInformation("Validating document schema for {DocumentTypeCount} document types...", docTypes.Count);

        var snapshot = await schema.GetSnapshotAsync(ct);
        var errors = new List<string>();

        foreach (var doc in docTypes)
        {
            foreach (var table in doc.Tables)
            {
                if (!snapshot.Tables.Contains(table.TableName))
                {
                    errors.Add($"Document type '{doc.TypeCode}': missing table '{table.TableName}'.");
                    continue;
                }

                if (!snapshot.ColumnsByTable.TryGetValue(table.TableName, out var columnList))
                {
                    errors.Add($"Document type '{doc.TypeCode}': cannot read columns for table '{table.TableName}'.");
                    continue;
                }

                // Build a quick lookup by column name (case-insensitive)
                var columns = columnList
                    .GroupBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                // Hybrid contract: all per-type tables must reference documents(id) via document_id.
                ValidateDocumentIdContract(doc.TypeCode, table, columns, snapshot, errors);

                // Metadata columns
                foreach (var col in table.Columns)
                {
                    if (!columns.TryGetValue(col.ColumnName, out var actual))
                    {
                        errors.Add($"Document type '{doc.TypeCode}': table '{table.TableName}' missing column '{col.ColumnName}'.");
                        continue;
                    }

                    // Nullability
                    if (col.Required && actual.IsNullable)
                        errors.Add($"Document type '{doc.TypeCode}': table '{table.TableName}' column '{col.ColumnName}' must be NOT NULL.");

                    // Type compatibility
                    if (!typeMapper.IsCompatible(col.Type, actual.DbType))
                    {
                        var expected = typeMapper.GetExpectedDbType(col.Type);
                        errors.Add($"Document type '{doc.TypeCode}': table '{table.TableName}' column '{col.ColumnName}' has type '{actual.DbType}', expected compatible with '{expected}'.");
                    }

                    // Length (diagnostic only)
                    if (col.MaxLength is not null
                        && actual.CharacterMaximumLength is not null
                        && actual.CharacterMaximumLength.Value < col.MaxLength.Value)
                    {
                        errors.Add($"Document type '{doc.TypeCode}': table '{table.TableName}' column '{col.ColumnName}' max length is {actual.CharacterMaximumLength}, expected >= {col.MaxLength}.");
                    }
                }

                // Indexes (best-effort)
                if (table.Indexes is { Count: > 0 })
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
                            errors.Add($"Document type '{doc.TypeCode}': table '{table.TableName}' missing index '{ix.Name}'.");
                            continue;
                        }

                        if (ix.Unique && !actualIx.IsUnique)
                            errors.Add($"Document type '{doc.TypeCode}': table '{table.TableName}' index '{ix.Name}' must be UNIQUE.");

                        var expectedCols = ix.ColumnNames.ToArray();
                        var actualCols = actualIx.ColumnNames.ToArray();

                        if (!ColumnsEqual(expectedCols, actualCols))
                        {
                            errors.Add(
                                $"Document type '{doc.TypeCode}': table '{table.TableName}' index '{ix.Name}' columns mismatch. " +
                                $"Expected [{string.Join(", ", expectedCols)}], got [{string.Join(", ", actualCols)}].");
                        }
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            logger.LogError("Document schema validation FAILED with {ErrorCount} errors in {ElapsedMs} ms.", errors.Count, sw.ElapsedMilliseconds);
            throw new DocumentSchemaValidationException("Document schema validation failed: " + string.Join("; ", errors));
        }

        logger.LogInformation("Document schema validation OK in {ElapsedMs} ms.", sw.ElapsedMilliseconds);
    }

    private static void ValidateDocumentIdContract(
        string typeCode,
        DocumentTableMetadata table,
        IReadOnlyDictionary<string, DbColumnSchema> columns,
        DbSchemaSnapshot snapshot,
        List<string> errors)
    {
        const string colName = "document_id";

        if (!columns.ContainsKey(colName))
        {
            errors.Add($"Document type '{typeCode}': table '{table.TableName}' must have '{colName}' column referencing documents(id).");
            return;
        }

        if (!snapshot.ForeignKeysByTable.TryGetValue(table.TableName, out var fks))
            fks = [];

        var hasFk = fks.Any(fk =>
            fk.ColumnName.Equals(colName, StringComparison.OrdinalIgnoreCase) &&
            fk.ReferencedTableName.Equals("documents", StringComparison.OrdinalIgnoreCase) &&
            fk.ReferencedColumnName.Equals("id", StringComparison.OrdinalIgnoreCase));

        if (!hasFk)
            errors.Add($"Document type '{typeCode}': table '{table.TableName}' must have FK on '{colName}' -> documents(id).");
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
