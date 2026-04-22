using System.Text.Json;
using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Internal;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportCellActions_P0Tests
{
    [Fact]
    public void BuildDocumentAction_Returns_Typed_Document_Action()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var action = ReportCellActions.BuildDocumentAction("pm.receivable_charge", id);

        action.Should().BeEquivalentTo(new ReportCellActionDto("open_document", DocumentType: "pm.receivable_charge", DocumentId: id));
    }

    [Fact]
    public void BuildAccountAction_Returns_Typed_Account_Action()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var action = ReportCellActions.BuildAccountAction(id);

        action.Should().BeEquivalentTo(new ReportCellActionDto("open_account", AccountId: id));
    }

    [Fact]
    public void BuildCatalogAction_Returns_Typed_Catalog_Action()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var action = ReportCellActions.BuildCatalogAction("pm.property", id);

        action.Should().BeEquivalentTo(new ReportCellActionDto("open_catalog", CatalogType: "pm.property", CatalogId: id));
    }

    [Fact]
    public void BuildReportAction_Returns_Typed_Report_Action()
    {
        var propertyId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var action = ReportCellActions.BuildReportAction(
            reportCode: "accounting.account_card",
            parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-03-01",
                ["to_utc"] = "2026-03-31"
            },
            filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["property_id"] = new(JsonSerializer.SerializeToElement(new[] { propertyId }), IncludeDescendants: true)
            });

        action.Kind.Should().Be(ReportCellActionKinds.OpenReport);
        action.Report.Should().NotBeNull();
        action.Report!.ReportCode.Should().Be("accounting.account_card");
        action.Report.Parameters.Should().Equal(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["from_utc"] = "2026-03-01",
            ["to_utc"] = "2026-03-31"
        });
        action.Report.Filters.Should().ContainKey("property_id");
        action.Report.Filters!["property_id"].IncludeDescendants.Should().BeTrue();
        action.Report.Filters["property_id"].Value.EnumerateArray().Select(x => x.GetGuid()).Should().Equal(propertyId);
    }

    [Fact]
    public void BuildAccountCardAction_Merges_Account_Filter_With_Inherited_Filters()
    {
        var accountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var propertyId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        var action = ReportCellActions.BuildAccountCardAction(
            accountId,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 3, 31),
            new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["property_id"] = new(JsonSerializer.SerializeToElement(new[] { propertyId }), IncludeDescendants: true)
            });

        action.Kind.Should().Be(ReportCellActionKinds.OpenReport);
        action.Report.Should().NotBeNull();
        action.Report!.ReportCode.Should().Be("accounting.account_card");
        action.Report.Parameters.Should().Equal(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["from_utc"] = "2026-03-01",
            ["to_utc"] = "2026-03-31"
        });
        action.Report.Filters.Should().ContainKeys("account_id", "property_id");
        action.Report.Filters!["account_id"].Value.GetGuid().Should().Be(accountId);
        action.Report.Filters["property_id"].IncludeDescendants.Should().BeTrue();
        action.Report.Filters["property_id"].Value.EnumerateArray().Select(x => x.GetGuid()).Should().Equal(propertyId);
    }
}
