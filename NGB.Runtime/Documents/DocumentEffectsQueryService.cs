using System.Text.Json;
using NGB.Contracts.Effects;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Core.Documents;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Posting;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents;

/// <summary>
/// Builds current effective document effects from the current posted document snapshot.
///
/// Semantics:
/// - Posted document => rebuild accounting / OR / RR effects in-memory via posting handlers.
/// - Non-posted document => no persisted effects.
///
/// This service intentionally does NOT read append-only subsystem history tables by document_id,
/// because history can contain prior Post/Unpost cycles and would over-report stale effects.
/// </summary>
public sealed class DocumentEffectsQueryService(
    IDocumentPostingActionResolver postingActionResolver,
    IAccountingPostingContextFactory accountingContextFactory,
    IDocumentOperationalRegisterPostingActionResolver operationalRegisterPostingActionResolver,
    IOperationalRegisterRepository operationalRegisters,
    IDocumentReferenceRegisterPostingActionResolver referenceRegisterPostingActionResolver,
    IReferenceRegisterRepository referenceRegisters,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : IDocumentEffectsQueryService
{
    public async Task<DocumentEffectsQueryResult> GetAsync(DocumentRecord record, int limit, CancellationToken ct)
    {
        if (record is null)
            throw new NgbArgumentRequiredException(nameof(record));

        if (limit <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be positive.");

        if (record.Status != DocumentStatus.Posted)
            return new DocumentEffectsQueryResult([], [], []);

        var accounting = await BuildAccountingEntriesAsync(record, limit, ct);
        var opreg = await BuildOperationalRegisterMovementsAsync(record, limit, ct);
        var refreg = await BuildReferenceRegisterWritesAsync(record, limit, ct);

        return new DocumentEffectsQueryResult(accounting, opreg, refreg);
    }

    private async Task<IReadOnlyList<AccountingEntryEffectDto>> BuildAccountingEntriesAsync(
        DocumentRecord record,
        int limit,
        CancellationToken ct)
    {
        var action = postingActionResolver.TryResolve(record);
        if (action is null)
            return [];

        var ctx = await accountingContextFactory.CreateAsync(ct);
        await action(ctx, ct);

        var rows = ctx.Entries.Take(limit).ToArray();
        if (rows.Length == 0)
            return [];

        var keys = rows
            .SelectMany(x => new[] { x.DebitDimensions, x.CreditDimensions })
            .CollectValueKeys();

        var resolved = keys.Count == 0
            ? new Dictionary<DimensionValueKey, string>()
            : await dimensionValueEnrichmentReader.ResolveAsync(keys, ct);

        var result = new List<AccountingEntryEffectDto>(rows.Length);
        long nextId = 1;

        foreach (var x in rows)
        {
            result.Add(new AccountingEntryEffectDto(
                EntryId: nextId++,
                DocumentId: x.DocumentId,
                OccurredAtUtc: x.Period,
                DebitAccount: new EffectAccountDto(x.Debit.Id, x.Debit.Code, x.Debit.Name),
                CreditAccount: new EffectAccountDto(x.Credit.Id, x.Credit.Code, x.Credit.Name),
                Amount: x.Amount,
                IsStorno: x.IsStorno,
                DebitDimensionSetId: x.DebitDimensionSetId,
                CreditDimensionSetId: x.CreditDimensionSetId,
                DebitDimensions: ToDimensionValues(x.DebitDimensions, resolved),
                CreditDimensions: ToDimensionValues(x.CreditDimensions, resolved)));
        }

        return result;
    }

    private async Task<IReadOnlyList<OperationalRegisterMovementEffectDto>> BuildOperationalRegisterMovementsAsync(
        DocumentRecord record,
        int limit,
        CancellationToken ct)
    {
        var action = operationalRegisterPostingActionResolver.TryResolve(record);
        if (action is null)
            return [];

        var builder = new OperationalRegisterMovementsBuilder(record.Id);
        await action(builder, ct);

        var movementsByRegister = builder.Build();
        if (movementsByRegister.Count == 0)
            return [];

        var registerIds = movementsByRegister.Keys.ToArray();
        var registerMap = (await operationalRegisters.GetByIdsAsync(registerIds, ct))
            .ToDictionary(x => x.RegisterId);

        var orderedRows = movementsByRegister
            .OrderBy(x => registerMap.TryGetValue(x.Key, out var reg)
                ? reg.CodeNorm
                : string.Empty, StringComparer.Ordinal)
            .SelectMany(x => x.Value.Select(m => (RegisterId: x.Key, Movement: m)))
            .Take(limit)
            .ToArray();

        if (orderedRows.Length == 0)
            return [];

        var bagsById = await ResolveBagsByIdsAsync(orderedRows.Select(x => x.Movement.DimensionSetId), ct);
        var resolved = await ResolveDisplaysAsync(bagsById.Values, ct);

        var result = new List<OperationalRegisterMovementEffectDto>(orderedRows.Length);
        long nextId = 1;

        foreach (var row in orderedRows)
        {
            if (!registerMap.TryGetValue(row.RegisterId, out var register))
                continue;

            var bag = bagsById.GetValueOrDefault(row.Movement.DimensionSetId, DimensionBag.Empty);
            result.Add(new OperationalRegisterMovementEffectDto(
                RegisterId: register.RegisterId,
                RegisterCode: register.Code,
                RegisterName: register.Name,
                MovementId: nextId++,
                DocumentId: row.Movement.DocumentId,
                OccurredAtUtc: row.Movement.OccurredAtUtc,
                PeriodMonth: new DateOnly(row.Movement.OccurredAtUtc.Year, row.Movement.OccurredAtUtc.Month, 1),
                IsStorno: false,
                DimensionSetId: row.Movement.DimensionSetId,
                Dimensions: ToDimensionValues(bag, resolved),
                Resources: row.Movement.Resources
                    .OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Select(x => new EffectResourceValueDto(x.Key, x.Value))
                    .ToArray()));
        }

        return result;
    }

    private async Task<IReadOnlyList<ReferenceRegisterWriteEffectDto>> BuildReferenceRegisterWritesAsync(
        DocumentRecord record,
        int limit,
        CancellationToken ct)
    {
        var action = referenceRegisterPostingActionResolver.TryResolve(record);
        if (action is null)
            return [];

        var builder = new ReferenceRegisterRecordsBuilder(record.Id);
        await action(builder, ReferenceRegisterWriteOperation.Post, ct);

        var recordsByRegister = builder.RecordsByRegister;
        if (recordsByRegister.Count == 0)
            return [];

        var registerIds = recordsByRegister.Keys.ToArray();
        var registerMap = (await referenceRegisters.GetByIdsAsync(registerIds, ct))
            .ToDictionary(x => x.RegisterId);

        var orderedRows = recordsByRegister
            .OrderBy(x => registerMap.TryGetValue(x.Key, out var reg)
                ? reg.CodeNorm
                : string.Empty, StringComparer.Ordinal)
            .SelectMany(x => x.Value.Select(r => (RegisterId: x.Key, Record: r)))
            .Take(limit)
            .ToArray();

        if (orderedRows.Length == 0)
            return [];

        var bagsById = await ResolveBagsByIdsAsync(orderedRows.Select(x => x.Record.DimensionSetId), ct);
        var resolved = await ResolveDisplaysAsync(bagsById.Values, ct);

        var result = new List<ReferenceRegisterWriteEffectDto>(orderedRows.Length);
        long nextId = 1;
        var recordedAtUtc = record.PostedAtUtc ?? record.UpdatedAtUtc;

        foreach (var row in orderedRows)
        {
            if (!registerMap.TryGetValue(row.RegisterId, out var register))
                continue;

            var bag = bagsById.GetValueOrDefault(row.Record.DimensionSetId, DimensionBag.Empty);
            var periodBucketUtc = row.Record.PeriodUtc.HasValue
                ? BuildPeriodBucketUtc(register.Periodicity, row.Record.PeriodUtc.Value)
                : null;

            result.Add(new ReferenceRegisterWriteEffectDto(
                RegisterId: register.RegisterId,
                RegisterCode: register.Code,
                RegisterName: register.Name,
                RecordId: nextId++,
                DocumentId: row.Record.RecorderDocumentId,
                PeriodUtc: row.Record.PeriodUtc,
                PeriodBucketUtc: periodBucketUtc,
                RecordedAtUtc: recordedAtUtc,
                DimensionSetId: row.Record.DimensionSetId,
                Dimensions: ToDimensionValues(bag, resolved),
                Fields: ToJsonFields(row.Record.Values),
                IsTombstone: row.Record.IsDeleted));
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<Guid, DimensionBag>> ResolveBagsByIdsAsync(
        IEnumerable<Guid> dimensionSetIds,
        CancellationToken ct)
    {
        var ids = dimensionSetIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, DimensionBag>();

        return await dimensionSetReader.GetBagsByIdsAsync(ids, ct);
    }

    private async Task<IReadOnlyDictionary<DimensionValueKey, string>> ResolveDisplaysAsync(
        IEnumerable<DimensionBag> bags,
        CancellationToken ct)
    {
        var keys = bags.CollectValueKeys();
        if (keys.Count == 0)
            return new Dictionary<DimensionValueKey, string>();

        return await dimensionValueEnrichmentReader.ResolveAsync(keys, ct);
    }

    private static DateTime? BuildPeriodBucketUtc(ReferenceRegisterPeriodicity periodicity, DateTime periodUtc)
        => periodicity switch
        {
            ReferenceRegisterPeriodicity.NonPeriodic => null,
            ReferenceRegisterPeriodicity.Day => new DateTime(periodUtc.Year, periodUtc.Month, periodUtc.Day, 0, 0, 0, DateTimeKind.Utc),
            ReferenceRegisterPeriodicity.Month => new DateTime(periodUtc.Year, periodUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            ReferenceRegisterPeriodicity.Year => new DateTime(periodUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => null
        };

    private static IReadOnlyDictionary<string, JsonElement> ToJsonFields(IReadOnlyDictionary<string, object?> values)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in values)
        {
            result[kv.Key] = JsonTools.Jobj(kv.Value);
        }

        return result;
    }

    private static IReadOnlyList<EffectDimensionValueDto> ToDimensionValues(
        DimensionBag bag,
        IReadOnlyDictionary<DimensionValueKey, string> resolved)
    {
        if (bag.IsEmpty)
            return [];

        return bag
            .Select(x =>
            {
                var key = new DimensionValueKey(x.DimensionId, x.ValueId);
                var display = resolved.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : ShortGuid(x.ValueId);

                return new EffectDimensionValueDto(x.DimensionId, x.ValueId, display);
            })
            .ToArray();
    }

    private static string ShortGuid(Guid valueId)
    {
        var s = valueId.ToString("N");
        return s.Length > 8 ? s[..8] : s;
    }
}
