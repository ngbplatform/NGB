using NGB.Contracts.Common;
using NGB.Contracts.Effects;
using NGB.Contracts.Metadata;

namespace NGB.Application.Abstractions.Services;

/// <summary>
/// Allows modules / vertical solutions to contribute UI-oriented action availability
/// for specific document types.
///
/// Example: Property Management receivables can enable/disable the "apply" action
/// based on current outstanding/credit amounts.
/// </summary>
public interface IDocumentUiEffectsContributor
{
    /// <summary>
    /// Returns a list of action contributions to be merged into the base UI action snapshot.
    /// Return an empty list when the contributor does not apply to this document.
    ///
    /// IMPORTANT:
    /// - Must be side-effect free.
    /// - Should be fast (bounded queries) and deterministic.
    /// </summary>
    Task<IReadOnlyList<DocumentUiActionContributionDto>> ContributeAsync(
        string documentType,
        Guid documentId,
        RecordPayload payload,
        DocumentStatus status,
        CancellationToken ct);
}
