using Dapper;
using FluentAssertions;
using NGB.Metadata.Base;
using NGB.Persistence.Documents.Universal;
using NGB.PropertyManagement.PostgreSql.Documents;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

public sealed class PropertyManagementDocumentListFilterSqlContributorFullCoverageTests
{
    [Theory]
    [MemberData(nameof(HandledFilters))]
    public void TryBuildClause_HandlesEverySupportedDocumentAndDerivedFilter(
        string documentType,
        string filterKey,
        string[] expectedFragments)
    {
        var parameters = new DynamicParameters();
        var sut = new PropertyManagementDocumentListFilterSqlContributor();

        var handled = sut.TryBuildClause(
            Head(documentType),
            new DocumentFilter(filterKey, ["value"], ColumnType.String),
            "d",
            "h",
            "filter_value",
            parameters,
            out var clause);

        handled.Should().BeTrue();
        clause.Should().NotBeNullOrWhiteSpace();
        clause.Should().Contain("EXISTS", Exactly.Once());
        clause.Should().Contain("@filter_value");
        foreach (var fragment in expectedFragments)
            clause.Should().Contain(fragment);
        parameters.ParameterNames.Should().ContainSingle().Which.Should().Be("filter_value");
    }

    [Theory]
    [MemberData(nameof(UnsupportedFilters))]
    public void TryBuildClause_RejectsUnsupportedKeysWithoutAddingParameters(string documentType)
    {
        var parameters = new DynamicParameters();
        var sut = new PropertyManagementDocumentListFilterSqlContributor();

        var handled = sut.TryBuildClause(
            Head(documentType),
            new DocumentFilter("unsupported_key", ["value"], ColumnType.String),
            "d",
            "h",
            "filter_value",
            parameters,
            out var clause);

        handled.Should().BeFalse();
        clause.Should().BeEmpty();
        parameters.ParameterNames.Should().BeEmpty();
    }

    public static TheoryData<string, string, string[]> HandledFilters => new()
    {
        { PropertyManagementCodes.Lease, "PARTY_ID", ["\"doc_pm_lease__parties\"", "lease_parties.\"party_id\"", "d.id"] },
        { PropertyManagementCodes.MaintenanceRequest, "lease_id", ["\"doc_pm_lease\"", "\"doc_pm_lease__parties\"", "h.\"property_id\"", "h.\"party_id\""] },
        { PropertyManagementCodes.WorkOrder, "property_id", ["\"doc_pm_maintenance_request\"", "request.\"property_id\"", "h.\"request_id\""] },
        { PropertyManagementCodes.WorkOrder, "party_id", ["\"doc_pm_maintenance_request\"", "request.\"party_id\"", "h.\"request_id\""] },
        { PropertyManagementCodes.WorkOrder, "category_id", ["\"doc_pm_maintenance_request\"", "request.\"category_id\"", "h.\"request_id\""] },
        { PropertyManagementCodes.WorkOrder, "priority", ["\"doc_pm_maintenance_request\"", "request.\"priority\"", "h.\"request_id\""] },
        { PropertyManagementCodes.WorkOrder, "lease_id", ["\"doc_pm_lease\"", "\"doc_pm_lease__parties\"", "\"doc_pm_maintenance_request\"", "h.\"request_id\""] },

        { PropertyManagementCodes.RentCharge, "property_id", ["\"doc_pm_lease\"", "lease.\"property_id\"", "h.\"lease_id\""] },
        { PropertyManagementCodes.ReceivableCharge, "property_id", ["\"doc_pm_lease\"", "lease.\"property_id\"", "h.\"lease_id\""] },
        { PropertyManagementCodes.LateFeeCharge, "property_id", ["\"doc_pm_lease\"", "lease.\"property_id\"", "h.\"lease_id\""] },
        { PropertyManagementCodes.ReceivablePayment, "property_id", ["\"doc_pm_lease\"", "lease.\"property_id\"", "h.\"lease_id\""] },
        { PropertyManagementCodes.ReceivableCreditMemo, "property_id", ["\"doc_pm_lease\"", "lease.\"property_id\"", "h.\"lease_id\""] },

        { PropertyManagementCodes.RentCharge, "party_id", ["\"doc_pm_lease__parties\"", "lease_parties.\"party_id\"", "h.\"lease_id\""] },
        { PropertyManagementCodes.ReceivableCharge, "party_id", ["\"doc_pm_lease__parties\"", "lease_parties.\"party_id\"", "h.\"lease_id\""] },
        { PropertyManagementCodes.LateFeeCharge, "party_id", ["\"doc_pm_lease__parties\"", "lease_parties.\"party_id\"", "h.\"lease_id\""] },
        { PropertyManagementCodes.ReceivablePayment, "party_id", ["\"doc_pm_lease__parties\"", "lease_parties.\"party_id\"", "h.\"lease_id\""] },
        { PropertyManagementCodes.ReceivableCreditMemo, "party_id", ["\"doc_pm_lease__parties\"", "lease_parties.\"party_id\"", "h.\"lease_id\""] },

        { PropertyManagementCodes.ReceivableReturnedPayment, "lease_id", ["\"doc_pm_receivable_payment\"", "payment.\"lease_id\"", "h.\"original_payment_id\""] },
        { PropertyManagementCodes.ReceivableReturnedPayment, "property_id", ["\"doc_pm_receivable_payment\"", "\"doc_pm_lease\"", "lease.\"property_id\""] },
        { PropertyManagementCodes.ReceivableReturnedPayment, "party_id", ["\"doc_pm_receivable_payment\"", "\"doc_pm_lease__parties\"", "lease_parties.\"party_id\""] },
        { PropertyManagementCodes.ReceivableReturnedPayment, "bank_account_id", ["\"doc_pm_receivable_payment\"", "payment.\"bank_account_id\"", "h.\"original_payment_id\""] }
    };

    public static TheoryData<string> UnsupportedFilters => new()
    {
        PropertyManagementCodes.Lease,
        PropertyManagementCodes.MaintenanceRequest,
        PropertyManagementCodes.WorkOrder,
        PropertyManagementCodes.RentCharge,
        PropertyManagementCodes.ReceivableCharge,
        PropertyManagementCodes.LateFeeCharge,
        PropertyManagementCodes.ReceivablePayment,
        PropertyManagementCodes.ReceivableCreditMemo,
        PropertyManagementCodes.ReceivableReturnedPayment,
        "pm.unknown"
    };

    private static DocumentHeadDescriptor Head(string typeCode) =>
        new(typeCode, "head_table", "display", []);
}
