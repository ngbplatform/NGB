using System.Text.Json;

namespace NGB.Contracts.Effects;

public sealed record EffectAccountDto(Guid AccountId, string Code, string Name);

public sealed record EffectDimensionValueDto(Guid DimensionId, Guid ValueId, string Display);

public sealed record EffectResourceValueDto(string Code, decimal Value);

public sealed record AccountingEntryEffectDto(
    long EntryId,
    Guid DocumentId,
    DateTime OccurredAtUtc,
    EffectAccountDto DebitAccount,
    EffectAccountDto CreditAccount,
    decimal Amount,
    bool IsStorno,
    Guid DebitDimensionSetId,
    Guid CreditDimensionSetId,
    IReadOnlyList<EffectDimensionValueDto> DebitDimensions,
    IReadOnlyList<EffectDimensionValueDto> CreditDimensions);

public sealed record OperationalRegisterMovementEffectDto(
    Guid RegisterId,
    string RegisterCode,
    string RegisterName,
    long MovementId,
    Guid DocumentId,
    DateTime OccurredAtUtc,
    DateOnly PeriodMonth,
    bool IsStorno,
    Guid DimensionSetId,
    IReadOnlyList<EffectDimensionValueDto> Dimensions,
    IReadOnlyList<EffectResourceValueDto> Resources);

public sealed record ReferenceRegisterWriteEffectDto(
    Guid RegisterId,
    string RegisterCode,
    string RegisterName,
    long RecordId,
    Guid? DocumentId,
    DateTime? PeriodUtc,
    DateTime? PeriodBucketUtc,
    DateTime RecordedAtUtc,
    Guid DimensionSetId,
    IReadOnlyList<EffectDimensionValueDto> Dimensions,
    IReadOnlyDictionary<string, JsonElement> Fields,
    bool IsTombstone);

public sealed record DocumentEffectsDto(
    IReadOnlyList<AccountingEntryEffectDto> AccountingEntries,
    IReadOnlyList<OperationalRegisterMovementEffectDto> OperationalRegisterMovements,
    IReadOnlyList<ReferenceRegisterWriteEffectDto> ReferenceRegisterWrites);
