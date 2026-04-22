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

/// <summary>
/// UI-oriented action availability snapshot for a document.
///
/// Rationale:
/// - Frontend should not guess what actions are allowed based on status and domain rules.
/// - Backend returns explicit flags + optional reasons that can be shown to the user.
///
/// NOTE:
/// This is intentionally minimal and grows over time.
/// </summary>
public sealed record DocumentUiEffectsDto(
    bool IsPosted,
    bool CanEdit,
    bool CanPost,
    bool CanUnpost,
    bool CanRepost,
    bool CanApply,
    IReadOnlyDictionary<string, IReadOnlyList<DocumentUiActionReasonDto>> DisabledReasons);

public sealed record DocumentUiActionReasonDto(string ErrorCode, string Message);

/// <summary>
/// Optional per-module contribution that can override a single UI action state (e.g., "apply" for receivables).
/// </summary>
public sealed record DocumentUiActionContributionDto(
    string Action,
    bool IsAllowed,
    IReadOnlyList<DocumentUiActionReasonDto> DisabledReasons);

public sealed record DocumentEffectsDto(
    IReadOnlyList<AccountingEntryEffectDto> AccountingEntries,
    IReadOnlyList<OperationalRegisterMovementEffectDto> OperationalRegisterMovements,
    IReadOnlyList<ReferenceRegisterWriteEffectDto> ReferenceRegisterWrites,
    DocumentUiEffectsDto? Ui = null);
