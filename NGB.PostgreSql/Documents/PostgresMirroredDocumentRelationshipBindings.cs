using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Documents;

internal sealed record PostgresMirroredDocumentRelationshipBindingExpectation(
    string DocumentTypeCode,
    string TableName,
    string ColumnName,
    string RelationshipCode,
    string ExpectedTriggerName)
{
    public string Descriptor =>
        $"document type '{DocumentTypeCode}' mirrored field '{TableName}.{ColumnName}' -> relationship '{RelationshipCode}'";

    public string ExpectedTriggerCallSnippet =>
        $"EXECUTE FUNCTION ngb_sync_mirrored_document_relationship('{ColumnName}', '{RelationshipCode}')";
}

internal sealed record PostgresTriggerBindingRow(
    string TableName,
    string TriggerName,
    string FunctionName,
    string TriggerDefinition);

internal static class PostgresMirroredDocumentRelationshipBindings
{
    private static readonly Regex InvalidIdentifierChars = new("[^a-zA-Z0-9_]+", RegexOptions.Compiled);

    public static IReadOnlyList<PostgresMirroredDocumentRelationshipBindingExpectation> EnumerateExpected(IDocumentTypeRegistry registry)
        => registry.GetAll()
            .SelectMany(doc => doc.Tables
                .Where(t => t.Kind == TableKind.Head)
                .SelectMany(t => t.Columns
                    .Where(c => c.MirroredRelationship is not null)
                    .Select(c => new PostgresMirroredDocumentRelationshipBindingExpectation(
                        DocumentTypeCode: doc.TypeCode,
                        TableName: t.TableName,
                        ColumnName: c.ColumnName,
                        RelationshipCode: c.MirroredRelationship!.RelationshipCode,
                        ExpectedTriggerName: ComputeTriggerName(c.ColumnName, c.MirroredRelationship.RelationshipCode)))))
            .Distinct()
            .ToArray();

    public static string ComputeTriggerName(string columnName, string relationshipCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipCode);

        var normalizedColumn = InvalidIdentifierChars.Replace(columnName, "_").ToLowerInvariant();
        if (normalizedColumn.Length > 20)
            normalizedColumn = normalizedColumn[..20];

        var hashInput = $"{columnName}|{relationshipCode.ToLowerInvariant()}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(hashInput));
        var hashHex = Convert.ToHexString(hash).ToLowerInvariant()[..8];

        return $"trg_docrel_mirror__{normalizedColumn}__{hashHex}";
    }

    public static async Task<IReadOnlyList<PostgresTriggerBindingRow>> LoadExistingBindingsAsync(
        IUnitOfWork uow,
        IReadOnlyCollection<string> tables,
        CancellationToken ct)
    {
        if (tables.Count == 0)
            return [];

        var rows = await uow.Connection.QueryAsync<PostgresTriggerBindingRow>(
            new CommandDefinition(
                """
                SELECT
                    cl.relname AS "TableName",
                    t.tgname AS "TriggerName",
                    p.proname AS "FunctionName",
                    pg_get_triggerdef(t.oid, true) AS "TriggerDefinition"
                FROM pg_trigger t
                JOIN pg_class cl ON cl.oid = t.tgrelid
                JOIN pg_namespace ns ON ns.oid = cl.relnamespace
                JOIN pg_proc p ON p.oid = t.tgfoid
                WHERE ns.nspname = 'public'
                  AND NOT t.tgisinternal
                  AND cl.relname = ANY(@Tables);
                """,
                new { Tables = tables.ToArray() },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public static IReadOnlyList<string> GetMissingBindings(
        IReadOnlyList<PostgresMirroredDocumentRelationshipBindingExpectation> expected,
        IReadOnlyList<PostgresTriggerBindingRow> existing)
    {
        if (expected.Count == 0)
            return [];

        var missing = new List<string>();

        foreach (var item in expected)
        {
            var found = existing.Any(x =>
                x.TableName.Equals(item.TableName, StringComparison.OrdinalIgnoreCase)
                && x.TriggerName.Equals(item.ExpectedTriggerName, StringComparison.OrdinalIgnoreCase)
                && x.FunctionName.Equals("ngb_sync_mirrored_document_relationship", StringComparison.OrdinalIgnoreCase)
                && x.TriggerDefinition.Contains(item.ExpectedTriggerCallSnippet, StringComparison.Ordinal));

            if (!found)
                missing.Add($"{item.Descriptor} is missing trigger binding '{item.ExpectedTriggerName}' on table '{item.TableName}'.");
        }

        return missing;
    }
}
