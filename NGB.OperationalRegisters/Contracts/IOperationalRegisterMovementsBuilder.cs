namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Collects movements for one or more Operational Registers as part of a document posting pipeline.
///
/// IMPORTANT:
/// - The builder does not perform any persistence.
/// - The runtime will apply movements in the same DB transaction as document posting.
/// - The builder does not create storno movements; Unpost/Repost storno behavior is handled by the Operational Registers write pipeline.
/// </summary>
public interface IOperationalRegisterMovementsBuilder
{
    void Add(string registerCode, OperationalRegisterMovement movement);
}
