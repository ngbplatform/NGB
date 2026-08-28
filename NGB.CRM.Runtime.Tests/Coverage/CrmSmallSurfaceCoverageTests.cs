using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.CRM.DependencyInjection;
using NGB.CRM.Documents.Numbering;
using NGB.CRM.Runtime.DependencyInjection;
using NGB.CRM.Runtime.Documents.Validation;
using NGB.CRM.Runtime.Reporting.Datasets;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.Definitions.Documents.Validation;

namespace NGB.CRM.Runtime.Tests.Coverage;

public sealed class CrmSmallSurfaceCoverageTests
{
    [Fact]
    public void ModuleRegistrations_AreIdempotentAndReturnTheSameCollection()
    {
        var services = new ServiceCollection();

        services.AddCrmModule().Should().BeSameAs(services);
        services.AddCrmModule().Should().BeSameAs(services);
        services.AddCrmRuntimeModule().Should().BeSameAs(services);
        services.AddCrmRuntimeModule().Should().BeSameAs(services);

        services.Count(descriptor => descriptor.ServiceType == typeof(CrmLeadIntakeNumberingPolicy)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IDocumentNumberingPolicy)
                                     && descriptor.ImplementationType == typeof(CrmLeadIntakeNumberingPolicy)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(ICrmSetupService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(CrmDemoSeedOptions)).Should().Be(1);
        var seedOptions = services.Single(descriptor => descriptor.ServiceType == typeof(CrmDemoSeedOptions))
            .ImplementationInstance.Should().BeOfType<CrmDemoSeedOptions>().Subject;
        seedOptions.GeneratedAccountCount.Should().Be(CrmDemoSeedOptions.ProductionGeneratedAccountCount);
        seedOptions.GeneratedOpportunityCycleCount.Should()
            .Be(CrmDemoSeedOptions.ProductionGeneratedOpportunityCycleCount);
        CrmDemoSeedOptions.MaxGeneratedAccountCount.Should().BeGreaterThan(seedOptions.GeneratedAccountCount);
        CrmDemoSeedOptions.MaxGeneratedOpportunityCycleCount.Should()
            .BeGreaterThan(seedOptions.GeneratedOpportunityCycleCount);
        services.Count(descriptor => descriptor.ServiceType == typeof(IDocumentPostValidator)
                                     && descriptor.ImplementationType == typeof(LeadIntakePostValidator)).Should().Be(1);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IDefinitionsContributor));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IReportDatasetSource)
                                                && descriptor.ImplementationType == typeof(CrmOperationalReportsDatasetSource));
    }

    [Fact]
    public void OperationalDatasetSource_ReturnsEveryCanonicalDataset()
    {
        new CrmOperationalReportsDatasetSource().GetDatasets()
            .Select(dataset => dataset.DatasetCode)
            .Should().Equal(
                CrmCodes.SalesPipelineReport,
                CrmCodes.OpportunityHistoryReport,
                CrmCodes.LeadConversionFunnelReport,
                CrmCodes.ActivitySummaryReport,
                CrmCodes.QuoteRegisterReport);
    }
}
