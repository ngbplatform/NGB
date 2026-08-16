using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.DependencyInjection;
using NGB.AgencyBilling.Runtime.DependencyInjection;
using NGB.AgencyBilling.Runtime.Derivations.Exceptions;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.AgencyBilling.Runtime.Reporting.Datasets;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.ReferenceRegisters.Contracts;

namespace NGB.AgencyBilling.Runtime.Tests.Coverage;

public sealed class AgencyBillingSmallSurfaceCoverageTests
{
    [Fact]
    public void ModuleRegistration_ReturnsSameCollectionAndRegistersExpectedContracts()
    {
        var services = new ServiceCollection();

        services.AddAgencyBillingModule().Should().BeSameAs(services);
        services.AddAgencyBillingRuntimeModule().Should().BeSameAs(services);

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IDefinitionsContributor));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IDocumentNumberingPolicy));
        services.Should().Contain(descriptor => descriptor.ImplementationType == typeof(AgencyBillingSetupService));
        services.Should().Contain(descriptor => descriptor.ImplementationType == typeof(SalesInvoicePostingHandler));
        services.Should().Contain(descriptor => descriptor.ImplementationType == typeof(AgencyBillingOperationalReportsDatasetSource));
    }

    [Fact]
    public void DefinitionBoundScoped_IsIdempotentForTheSameContractAndImplementation()
    {
        var services = new ServiceCollection();

        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TestNumberingPolicy>();
        services.AddDefinitionBoundScoped<IDocumentNumberingPolicy, TestNumberingPolicy>();

        services.Count(descriptor => descriptor.ServiceType == typeof(TestNumberingPolicy)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IDocumentNumberingPolicy)).Should().Be(1);
    }

    [Fact]
    public void OperationalReportDatasetSource_ReturnsAllFiveDatasets()
    {
        new AgencyBillingOperationalReportsDatasetSource().GetDatasets()
            .Select(dataset => dataset.DatasetCode)
            .Should().Equal(
                AgencyBillingCodes.UnbilledTimeReport,
                AgencyBillingCodes.ProjectProfitabilityReport,
                AgencyBillingCodes.InvoiceRegisterReport,
                AgencyBillingCodes.ArAgingReport,
                AgencyBillingCodes.TeamUtilizationReport);
    }

    [Fact]
    public void InvoiceDraftAlreadyExistsException_ExposesStructuredFormError()
    {
        var timesheetId = Guid.CreateVersion7();

        var exception = new AgencyBillingInvoiceDraftAlreadyExistsException(timesheetId);

        exception.Context["sourceTimesheetId"].Should().Be(timesheetId);
        exception.Context["errors"].Should().BeAssignableTo<IReadOnlyDictionary<string, string[]>>()
            .Which["_form"].Should().ContainSingle().Which.Should().Contain("already exists");
    }

    [Fact]
    public async Task ClientContractReferencePostingHandler_IsAnExplicitNoOp()
    {
        var handler = new ClientContractReferenceRegisterPostingHandler();

        handler.TypeCode.Should().Be(AgencyBillingCodes.ClientContract);
        await handler.BuildRecordsAsync(null!, ReferenceRegisterWriteOperation.Post, null!, CancellationToken.None);
    }

    private sealed class TestNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => "test";
        public bool EnsureNumberOnCreateDraft => true;
        public bool EnsureNumberOnPost => false;
    }
}
