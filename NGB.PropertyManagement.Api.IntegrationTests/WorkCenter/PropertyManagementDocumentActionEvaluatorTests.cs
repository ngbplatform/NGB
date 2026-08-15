using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.PropertyManagement.Runtime.DocumentActions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.WorkCenter;

public sealed class PropertyManagementDocumentActionEvaluatorTests
{
    [Fact]
    public async Task Availability_uses_safe_default_when_configured_reasons_have_the_wrong_type()
    {
        var evaluator = new PropertyManagementApplyAvailabilityEvaluator();

        var result = await evaluator.EvaluateAsync(
            CreateContext(new Dictionary<string, object?>
            {
                ["pm.apply.disabled_reasons"] = "malformed"
            }),
            CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.DisabledReasons.Should().ContainSingle()
            .Which.Code.Should().Be("pm.apply.unavailable");
    }

    [Fact]
    public async Task Availability_returns_configured_reasons_when_they_are_well_formed()
    {
        var configured = new[]
        {
            new DocumentActionDisabledReasonDto("pm.apply.closed-period", "The period is closed.")
        };
        var evaluator = new PropertyManagementApplyAvailabilityEvaluator();

        var result = await evaluator.EvaluateAsync(
            CreateContext(new Dictionary<string, object?>
            {
                ["pm.apply.disabled_reasons"] = configured
            }),
            CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.DisabledReasons.Should().BeEquivalentTo(configured);
    }

    [Fact]
    public async Task Availability_uses_safe_default_when_reasons_are_not_configured()
    {
        var evaluator = new PropertyManagementApplyAvailabilityEvaluator();

        var result = await evaluator.EvaluateAsync(
            CreateContext(new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        result.DisabledReasons.Should().ContainSingle()
            .Which.Code.Should().Be("pm.apply.unavailable");
    }

    [Fact]
    public async Task Availability_allows_apply_when_the_enriched_fact_is_true()
    {
        var evaluator = new PropertyManagementApplyAvailabilityEvaluator();

        var result = await evaluator.EvaluateAsync(
            CreateContext(new Dictionary<string, object?>
            {
                ["pm.apply.allowed"] = true
            }),
            CancellationToken.None);

        result.Should().BeSameAs(DocumentActionAvailabilityResult.Allowed);
    }

    private static DocumentActionEvaluationContext CreateContext(IReadOnlyDictionary<string, object?> facts)
    {
        var document = new DocumentRecord
        {
            Id = Guid.NewGuid(),
            TypeCode = PropertyManagementCodes.ReceivablePayment,
            DateUtc = DateTime.UtcNow,
            Status = NGB.Core.Documents.DocumentStatus.Posted,
            Version = 1
        };
        var dto = new DocumentDto(
            document.Id,
            "Payment",
            new RecordPayload(),
            NGB.Contracts.Metadata.DocumentStatus.Posted,
            false);
        return new DocumentActionEvaluationContext(
            document,
            dto,
            new DocumentActionSecurityContext(
                Guid.NewGuid(),
                true,
                true,
                true,
                new HashSet<NGB.Core.Security.NgbPermissionKey>()),
            facts);
    }
}
