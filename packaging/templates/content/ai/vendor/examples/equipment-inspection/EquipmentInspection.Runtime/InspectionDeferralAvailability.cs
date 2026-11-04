using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Core.Documents;

namespace EquipmentInspection.Runtime;

public sealed class InspectionDeferralAvailability : IDocumentActionAvailabilityEvaluator
{
    public ValueTask<DocumentActionAvailabilityResult> EvaluateAsync(DocumentActionEvaluationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var reasons = new List<DocumentActionDisabledReasonDto>();
        if (context.Document.Status != DocumentStatus.Draft)
            reasons.Add(new("inspection.deferral.not-draft", "Only draft inspections can be deferred."));
        if (!context.Facts.TryGetValue(InspectionDeferralDefinitions.EquipmentSelectedFact, out var selected) || selected is not true)
            reasons.Add(new("inspection.deferral.equipment-required", "Select equipment before deferring the inspection."));
        if (context.Document.DateUtc > DateTime.MaxValue.AddDays(-1))
            reasons.Add(new("inspection.deferral.date-limit", "The inspection date cannot be advanced by another day."));
        return ValueTask.FromResult(new DocumentActionAvailabilityResult(reasons));
    }
}
