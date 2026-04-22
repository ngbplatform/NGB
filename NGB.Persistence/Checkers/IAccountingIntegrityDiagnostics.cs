namespace NGB.Persistence.Checkers;

public interface IAccountingIntegrityDiagnostics
{
    /// <summary>
    /// Returns the number of mismatched rows between:
    /// - stored monthly turnovers (accounting_turnovers)
    /// - aggregation of register_main for the same month (accounting_register_main)
    /// </summary>
    Task<long> GetTurnoversVsRegisterDiffCountAsync(DateOnly period, CancellationToken ct = default);
}
