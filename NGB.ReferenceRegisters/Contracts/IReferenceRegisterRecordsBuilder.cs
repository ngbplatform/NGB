namespace NGB.ReferenceRegisters.Contracts;

/// <summary>
/// Collects records for one or more Reference Registers as part of a document posting pipeline.
///
/// IMPORTANT:
/// - The builder does not perform any persistence.
/// - The runtime will apply records in the same DB transaction as document posting.
/// - For Unpost/Repost, handlers may emit tombstone records (<see cref="ReferenceRegisterRecordWrite.IsDeleted"/>)
///   to model removal, because Reference Registers are append-only.
/// </summary>
public interface IReferenceRegisterRecordsBuilder
{
    void Add(string registerCode, ReferenceRegisterRecordWrite record);
}
