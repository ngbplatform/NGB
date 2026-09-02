using FluentAssertions;
using Moq;
using NGB.CRM.Reporting;
using NGB.CRM.Runtime.Dashboard;

namespace NGB.CRM.Runtime.Tests.Dashboard;

public sealed class CrmDashboardServiceTests
{
    [Fact]
    public async Task GetAsync_MapsBoundedReaderSnapshotAndForwardsCancellation()
    {
        var asOf = new DateOnly(2026, 8, 31);
        var opportunityId = Guid.CreateVersion7();
        using var cts = new CancellationTokenSource();
        var reader = new Mock<ICrmDashboardReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetAsync(asOf, 6, cts.Token))
            .ReturnsAsync(new CrmDashboardSnapshot(
                1_000m,
                600m,
                12,
                7,
                4,
                250m,
                3,
                9,
                [new CrmDashboardOpportunitySnapshot(
                    opportunityId,
                    "Enterprise rollout",
                    "Acme",
                    "Proposal",
                    1_000m,
                    600m)]));

        var result = await new CrmDashboardService(reader.Object).GetAsync(asOf, cts.Token);

        result.AsOfUtc.Should().Be(asOf);
        result.PipelineAmount.Should().Be(1_000m);
        result.WeightedPipelineAmount.Should().Be(600m);
        result.LeadCount.Should().Be(12);
        result.QualifiedLeadCount.Should().Be(7);
        result.ConvertedLeadCount.Should().Be(4);
        result.QuoteAmount.Should().Be(250m);
        result.QuoteCount.Should().Be(3);
        result.ActivityCount.Should().Be(9);
        result.OpenOpportunities.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            OpportunityId = opportunityId,
            Opportunity = "Enterprise rollout",
            Account = "Acme",
            Stage = "Proposal",
            Amount = 1_000m,
            WeightedAmount = 600m
        });
        reader.VerifyAll();
    }
}
