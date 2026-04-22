using NGB.OperationalRegisters.Contracts;

namespace NGB.Runtime.OperationalRegisters;

/// <summary>
/// High-level UI/report oriented read service for Operational Registers.
///
/// This facade:
/// - validates common invariants (month ranges, page sizes),
/// - canonicalizes dimension filters,
/// - provides simple paging envelopes for UI consumers.
/// </summary>
public interface IOperationalRegisterReadService
{
    Task<OperationalRegisterMovementsPage> GetMovementsPageAsync(
        OperationalRegisterMovementsPageRequest request,
        CancellationToken ct = default);

    Task<OperationalRegisterMonthlyProjectionPage> GetTurnoversPageAsync(
        OperationalRegisterMonthlyProjectionPageRequest request,
        CancellationToken ct = default);

    Task<OperationalRegisterMonthlyProjectionPage> GetBalancesPageAsync(
        OperationalRegisterMonthlyProjectionPageRequest request,
        CancellationToken ct = default);
}
