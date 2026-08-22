using FluentAssertions;
using Moq;
using NGB.Api.Models;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.AgencyBilling.Api.Services;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Services;

public sealed class AgencyBillingMainMenuContributorFullCoverageTests
{
    [Fact]
    public async Task ContributeAsync_AllResourcesAndLinks_ReturnsCompleteOrderedMenu()
    {
        var catalogs = CatalogRegistry(
            AgencyBillingCodes.Client,
            AgencyBillingCodes.TeamMember,
            AgencyBillingCodes.Project,
            AgencyBillingCodes.RateCard,
            AgencyBillingCodes.ServiceItem,
            AgencyBillingCodes.PaymentTerms,
            AgencyBillingCodes.AccountingPolicy);
        var documents = DocumentRegistry(
            AgencyBillingCodes.ClientContract,
            AgencyBillingCodes.Timesheet,
            AgencyBillingCodes.SalesInvoice,
            AgencyBillingCodes.CustomerPayment);
        var reports = ReportProvider(
            AgencyBillingCodes.UnbilledTimeReport,
            AgencyBillingCodes.ProjectProfitabilityReport,
            AgencyBillingCodes.InvoiceRegisterReport,
            AgencyBillingCodes.ArAgingReport,
            AgencyBillingCodes.TeamUtilizationReport);
        var sut = new AgencyBillingMainMenuContributor(
            catalogs.Object,
            documents.Object,
            reports.Object,
            new ExternalLinksSettings("https://health", "https://jobs"));

        var result = await sut.ContributeAsync(CancellationToken.None);

        result.Select(x => x.Label).Should().Equal(
            "Dashboard", "Portfolio", "Operations", "Billing", "Reports", "Setup & Controls");
        result.SelectMany(x => x.Items).Should().Contain(x => x.Code == AgencyBillingCodes.Watchdog && x.Kind == "external");
        result.SelectMany(x => x.Items).Should().Contain(x => x.Code == AgencyBillingCodes.BackgroundJobs && x.Route == "https://jobs");
        result.Should().OnlyContain(x => x.Items.Select(item => item.Ordinal).SequenceEqual(x.Items.Select(item => item.Ordinal).Order()));
        reports.Verify(x => x.GetAllDefinitionsAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ContributeAsync_MissingOptionalResources_OmitsItemsAndEmptyGroups()
    {
        var catalogs = CatalogRegistry();
        var documents = DocumentRegistry();
        var reports = ReportProvider();
        var sut = new AgencyBillingMainMenuContributor(
            catalogs.Object,
            documents.Object,
            reports.Object,
            new ExternalLinksSettings(" ", ""));

        var result = await sut.ContributeAsync(CancellationToken.None);

        result.Should().ContainSingle().Which.Label.Should().Be("Dashboard");
        result.Single().Items.Should().ContainSingle().Which.Code.Should().Be(AgencyBillingCodes.DashboardOverviewReport);
    }

    private static Mock<ICatalogTypeRegistry> CatalogRegistry(params string[] codes)
    {
        var registry = new Mock<ICatalogTypeRegistry>();
        registry.Setup(x => x.All()).Returns(codes
            .Select(code => new CatalogTypeMetadata(
                code.ToUpperInvariant(),
                code,
                [],
                new CatalogPresentationMetadata("table", "display"),
                new CatalogMetadataVersion(1, "hash")))
            .ToArray());
        return registry;
    }

    private static Mock<IDocumentTypeRegistry> DocumentRegistry(params string[] codes)
    {
        var registry = new Mock<IDocumentTypeRegistry>();
        registry.Setup(x => x.GetAll()).Returns(codes
            .Select(code => new DocumentTypeMetadata(code.ToUpperInvariant(), []))
            .ToArray());
        return registry;
    }

    private static Mock<IReportDefinitionProvider> ReportProvider(params string[] codes)
    {
        var provider = new Mock<IReportDefinitionProvider>();
        provider.Setup(x => x.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes.Select(code => new ReportDefinitionDto(code.ToUpperInvariant(), code)).ToArray());
        return provider;
    }
}
