using NGB.OperationalRegisters.Contracts;

namespace NGB.PostgreSql.OperationalRegisters.Internal;

internal static class OperationalRegisterSnapshotSql
{
    // Finalization is the authority for snapshot freshness, including valid empty
    // snapshots whose zero rows were pruned. A dirty/blocked earlier month also
    // invalidates cumulative snapshots after it. The remaining SQL rolls forward
    // movements from this safe baseline, or all movements when there is none.
    // periodPredicate is an internal SQL fragment; never pass user input here.
    internal static string LatestFinalizedPeriod(string periodPredicate = "TRUE") => $"""
        SELECT MAX(finalized.period) AS period_month
        FROM operational_register_finalizations finalized
        WHERE finalized.register_id = @RegisterId
          AND finalized.status = {(short)OperationalRegisterFinalizationStatus.Finalized}
          AND {periodPredicate}
          AND NOT EXISTS (
              SELECT 1
              FROM operational_register_finalizations invalidated
              WHERE invalidated.register_id = finalized.register_id
                AND invalidated.period <= finalized.period
                AND invalidated.status <> {(short)OperationalRegisterFinalizationStatus.Finalized}
          )
        """;
}
