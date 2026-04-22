using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.ReferenceRegisters;

internal sealed class ReferenceRegisterRecordsBuilder(Guid documentId) : IReferenceRegisterRecordsBuilder
{
    private readonly Dictionary<Guid, List<ReferenceRegisterRecordWrite>> _map = new();

    public IReadOnlyDictionary<Guid, IReadOnlyList<ReferenceRegisterRecordWrite>> RecordsByRegister
        => _map.ToDictionary(
            static x => x.Key,
            static IReadOnlyList<ReferenceRegisterRecordWrite> (x) => x.Value);

    public void Add(string registerCode, ReferenceRegisterRecordWrite record)
    {
        if (string.IsNullOrWhiteSpace(registerCode))
            throw new NgbArgumentRequiredException(nameof(registerCode));
        
        if (record is null)
            throw new NgbArgumentRequiredException(nameof(record));
        
        // If the record explicitly references a recorder document, it MUST match the document being posted.
        if (record.RecorderDocumentId is not null
            && record.RecorderDocumentId.Value != Guid.Empty
            && record.RecorderDocumentId.Value != documentId)
        {
            throw new ReferenceRegisterRecordsValidationException(
                registerId: ReferenceRegisterId.FromCode(registerCode),
                reason: "recorder_document_id_mismatch",
                details: new { expectedDocumentId = documentId, actualRecorderDocumentId = record.RecorderDocumentId.Value });
        }

        var registerId = ReferenceRegisterId.FromCode(registerCode);
        if (!_map.TryGetValue(registerId, out var list))
        {
            list = [];
            _map.Add(registerId, list);
        }

        list.Add(record);
    }
}
