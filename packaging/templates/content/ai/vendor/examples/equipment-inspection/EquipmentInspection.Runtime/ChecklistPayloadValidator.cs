using System.Text.Json;
using EquipmentInspection.Definitions;
using NGB.Contracts.Common;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Exceptions;

namespace EquipmentInspection.Runtime;

public sealed class ChecklistPayloadValidator : IDocumentDraftPayloadValidator
{
    public string TypeCode => ChecklistDefinitions.DocumentCode;

    public Task ValidateCreateDraftPayloadAsync(
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        return ValidateAsync(payload, typedPartRowsByPartCode, ct);
    }

    public Task ValidateUpdateDraftPayloadAsync(
        Guid documentId,
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        return ValidateAsync(payload, typedPartRowsByPartCode, ct);
    }

    private static Task ValidateAsync(
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> parts,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Head values are still JSON here; omitted fields retain their saved value.
        if (payload.Fields is not null)
        {
            foreach (var (name, value) in payload.Fields)
            {
                if (string.Equals(name, "display", StringComparison.OrdinalIgnoreCase)
                    && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    ValidateTextLength(value.ToString(), "display");
                }
            }
        }

        if (!parts.TryGetValue(ChecklistDefinitions.ChecksPartCode, out var rows))
        {
            return Task.CompletedTask;
        }

        var lineNumbers = new HashSet<int>();
        foreach (var row in rows)
        {
            // Part values have already been converted according to metadata.
            if (row.TryGetValue("description", out var description))
            {
                ValidateTextLength(description as string, "checks.description");
            }

            // Metadata parsing has already required and converted line_no to Int32.
            var lineNumber = (int)row["line_no"]!;
            if (lineNumber <= 0 || !lineNumbers.Add(lineNumber))
            {
                throw new NgbArgumentInvalidException("checks.line_no", "Check line numbers must be positive and unique.");
            }
        }

        return Task.CompletedTask;
    }

    private static void ValidateTextLength(string? text, string field)
    {
        if (text is { Length: > ChecklistDefinitions.TextMaxLength })
        {
            throw new NgbArgumentInvalidException(field,
                $"Use at most {ChecklistDefinitions.TextMaxLength} characters.");
        }
    }
}
