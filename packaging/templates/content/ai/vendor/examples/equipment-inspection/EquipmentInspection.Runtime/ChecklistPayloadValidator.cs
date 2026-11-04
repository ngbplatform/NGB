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
        return ValidateAsync(typedPartRowsByPartCode, ct);
    }

    public Task ValidateUpdateDraftPayloadAsync(
        Guid documentId,
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        return ValidateAsync(typedPartRowsByPartCode, ct);
    }

    private static Task ValidateAsync(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> parts,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!parts.TryGetValue(ChecklistDefinitions.ChecksPartCode, out var rows))
        {
            return Task.CompletedTask;
        }

        var lineNumbers = new HashSet<int>();
        foreach (var row in rows)
        {
            // Metadata parsing has already required and converted line_no to Int32.
            var lineNumber = (int)row["line_no"]!;
            if (lineNumber <= 0 || !lineNumbers.Add(lineNumber))
            {
                throw new NgbArgumentInvalidException("checks.line_no", "Check line numbers must be positive and unique.");
            }
        }

        return Task.CompletedTask;
    }
}
