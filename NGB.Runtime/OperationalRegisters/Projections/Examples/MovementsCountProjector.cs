using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.OperationalRegisters.Projections.Examples;

/// <summary>
/// Reference implementation of a projector that builds very simple projections:
/// - turnovers: net movement count per (month, dimension_set)
/// - balances: same payload (for demonstration)
///
/// Notes:
/// - Uses semantics: storno movements are counted as -1.
/// - Expects the register to have a numeric resource with column_code <c>movement_count</c>.
///
/// This projector is NOT registered by default. It exists as an example and as a handy
/// building block for integration tests.
/// </summary>
public sealed class MovementsCountProjector(
    string registerCodeNorm,
    IOperationalRegisterTurnoversStore turnovers,
    IOperationalRegisterBalancesStore balances)
    : IOperationalRegisterMonthProjector
{
    private const string MovementCountColumn = "movement_count";

    public string RegisterCodeNorm { get; } = (registerCodeNorm ?? throw new NgbArgumentRequiredException(nameof(registerCodeNorm)))
        .Trim()
        .ToLowerInvariant();

    public async Task RebuildMonthAsync(
        OperationalRegisterMonthProjectionContext context,
        CancellationToken ct = default)
    {
        if (context.RegisterId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(context));

        // IMPORTANT (deadlock avoidance): ensure derived tables *before* reading movements.
        // If a concurrent EnsureSchema() is running (e.g., admin maintenance), it may need
        // ACCESS EXCLUSIVE on the movements table. If we first read movements (ACCESS SHARE)
        // and only then attempt to ensure turnovers/balances (which takes the same schema lock
        // namespace), we can form a deadlock cycle:
        //   - DDL holds schema lock and waits for ACCESS EXCLUSIVE (blocked by our read)
        //   - We wait for schema lock (blocked by DDL)
        // Taking the schema lock up-front prevents DDL from starting while we hold ACCESS SHARE.
        await turnovers.EnsureSchemaAsync(context.RegisterId, ct);
        await balances.EnsureSchemaAsync(context.RegisterId, ct);

        // Read movements for the month with paging.
        // We aggregate net count (storno => -1).
        var counts = new Dictionary<Guid, long>();

        long? after = null;
        while (true)
        {
            var page = await context.Movements.GetByMonthAsync(
                context.RegisterId,
                context.PeriodMonth,
                dimensionSetId: null,
                afterMovementId: after,
                limit: 2000,
                ct: ct);

            if (page.Count == 0)
                break;

            foreach (var m in page)
            {
                var delta = m.IsStorno ? -1L : 1L;

                if (!counts.TryAdd(m.DimensionSetId, delta))
                    counts[m.DimensionSetId] += delta;

                after = m.MovementId;
            }
        }

        var rows = counts
            .Where(kv => kv.Value != 0)
            .OrderBy(kv => kv.Key)
            .Select(kv => new OperationalRegisterMonthlyProjectionRow(
                kv.Key,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    [MovementCountColumn] = kv.Value
                }))
            .ToArray();

        await turnovers.ReplaceForMonthAsync(context.RegisterId, context.PeriodMonth, rows, ct);
        await balances.ReplaceForMonthAsync(context.RegisterId, context.PeriodMonth, rows, ct);
    }
}
