using FluentAssertions;
using NGB.Accounting.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Datasets;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class AccountingLedgerAnalysisDatasetModel_P0Tests
{
    [Fact]
    public void Create_WhenSharedPlatformDataset_UsesStableSystemFields_And_DoesNotContainPmSpecificFieldsOrLookups()
    {
        var dataset = AccountingLedgerAnalysisDatasetModel.Create();

        dataset.DatasetCode.Should().Be(AccountingLedgerAnalysisDatasetModel.DatasetCode);
        dataset.Fields.Should().NotBeNull();
        dataset.Measures.Should().NotBeNull();

        var fields = dataset.Fields!.ToList();
        var fieldCodes = fields.Select(x => x.Code).ToList();

        fieldCodes.Should().Contain("posting_side");
        fieldCodes.Should().NotContain("property_id");
        fieldCodes.Should().NotContain("party_id");
        fieldCodes.Should().NotContain("lease_id");

        var postingSideField = fields.Single(x => x.Code == "posting_side");
        postingSideField.Kind.Should().Be(ReportFieldKind.System);
        postingSideField.IsFilterable.Should().BeFalse();
        postingSideField.IsGroupable.Should().BeFalse();
        postingSideField.IsSortable.Should().BeFalse();
        postingSideField.IsSelectable.Should().BeFalse();

        var catalogLookupTypes = fields
            .Where(x => x.Lookup is CatalogLookupSourceDto)
            .Select(x => ((CatalogLookupSourceDto)x.Lookup!).CatalogType)
            .ToList();

        catalogLookupTypes.Should().NotContain(x => x.Equals("pm.property", StringComparison.OrdinalIgnoreCase));
        catalogLookupTypes.Should().NotContain(x => x.Equals("pm.party", StringComparison.OrdinalIgnoreCase));

        var documentField = fields.Single(x => x.Code == "document_id");
        documentField.Lookup.Should().BeOfType<DocumentLookupSourceDto>();

        var documentTypes = ((DocumentLookupSourceDto)documentField.Lookup!).DocumentTypes.ToList();
        documentTypes.Should().ContainSingle();
        documentTypes.Single().Should().Be(AccountingDocumentTypeCodes.GeneralJournalEntry);
        documentTypes.Should().NotContain(x => x.StartsWith("pm.", StringComparison.OrdinalIgnoreCase));

        var measureCodes = dataset.Measures!.Select(x => x.Code).ToList();
        measureCodes.Should().Equal("debit_amount", "credit_amount", "net_amount");
    }
}
