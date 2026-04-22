using NGB.OperationalRegisters.Contracts;

namespace NGB.Persistence.OperationalRegisters;

public interface IOperationalRegisterMovementsReader
{
    Task<IReadOnlyList<OperationalRegisterMovementRead>> GetByMonthAsync(
        Guid registerId,
        DateOnly periodMonth,
        Guid? dimensionSetId = null,
        long? afterMovementId = null,
        int limit = 1000,
        CancellationToken ct = default);

    Task<IReadOnlyList<DateOnly>> GetDistinctMonthsByDocumentAsync(
        Guid registerId,
        Guid documentId,
        CancellationToken ct = default);
}
