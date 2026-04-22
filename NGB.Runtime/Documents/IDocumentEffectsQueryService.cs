using NGB.Contracts.Effects;
using NGB.Core.Documents;

namespace NGB.Runtime.Documents;

public interface IDocumentEffectsQueryService
{
    Task<DocumentEffectsQueryResult> GetAsync(DocumentRecord record, int limit, CancellationToken ct);
}

public sealed record DocumentEffectsQueryResult(
    IReadOnlyList<AccountingEntryEffectDto> AccountingEntries,
    IReadOnlyList<OperationalRegisterMovementEffectDto> OperationalRegisterMovements,
    IReadOnlyList<ReferenceRegisterWriteEffectDto> ReferenceRegisterWrites);
