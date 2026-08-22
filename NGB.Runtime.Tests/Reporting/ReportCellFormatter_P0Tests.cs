using FluentAssertions;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Rendering;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportCellFormatter_P0Tests
{
    [Fact]
    public void BuildCellAndBlankCell_PreserveStylesRolesActionsAndNulls()
    {
        var sut = new ReportCellFormatter();
        var action = new ReportCellActionDto(ReportCellActionKinds.OpenCatalog, CatalogType: "catalog", CatalogId: Guid.NewGuid());
        var column = new ReportSheetColumnDto("value", "Value", "string", SemanticRole: "column-role");

        var cell = sut.BuildCell("text", column, "accent", "override-role", action);
        var nullCell = sut.BuildCell(null, column);
        var inheritedRoleBlank = sut.BuildBlankCell(column, "muted");
        var overriddenRoleBlank = sut.BuildBlankCell(column, semanticRole: "blank-role");
        var label = sut.BuildLabelCell("Label", "header", "label-role", 2, 3, action);

        cell.Value!.Value.GetString().Should().Be("text");
        cell.Display.Should().Be("text");
        cell.StyleKey.Should().Be("accent");
        cell.SemanticRole.Should().Be("override-role");
        cell.Action.Should().BeSameAs(action);
        nullCell.Value.Should().BeNull();
        nullCell.Display.Should().BeNull();
        inheritedRoleBlank.Value.Should().BeNull();
        inheritedRoleBlank.Display.Should().BeNull();
        inheritedRoleBlank.StyleKey.Should().Be("muted");
        inheritedRoleBlank.SemanticRole.Should().Be("column-role");
        overriddenRoleBlank.SemanticRole.Should().Be("blank-role");
        label.Display.Should().Be("Label");
        label.ColSpan.Should().Be(2);
        label.RowSpan.Should().Be(3);
        label.Action.Should().BeSameAs(action);
    }

    [Fact]
    public void FormatDisplay_CoversNullTimeNumericAndFallbackValues()
    {
        var sut = new ReportCellFormatter();

        sut.FormatDisplay(null).Should().BeNull();
        sut.FormatDisplay(new TimeOnly(13, 14, 15)).Should().Be("01:14:15 PM");
        sut.FormatDisplay(12.5m).Should().Be("12.5");
        sut.FormatDisplay(12.5d).Should().Be("12.5");
        sut.FormatDisplay(12.5f).Should().Be("12.5");
        sut.FormatDisplay(42).Should().Be("42");
        sut.FormatGroupLabel(null).Should().Be("(blank)");
        sut.FormatGroupLabel("   ").Should().Be("(blank)");
        sut.FormatGroupLabel("value").Should().Be("value");
    }

    [Theory]
    [InlineData(null, "03/14/2026 01:02:03 PM")]
    [InlineData(ReportTimeGrain.Day, "03/14/2026")]
    [InlineData(ReportTimeGrain.Week, "Week of 03/14/2026")]
    [InlineData(ReportTimeGrain.Month, "March 2026")]
    [InlineData(ReportTimeGrain.Quarter, "Q1 2026")]
    [InlineData(ReportTimeGrain.Year, "2026")]
    public void FormatDisplay_DateTime_CoversEveryTimeGrain(ReportTimeGrain? grain, string expected)
    {
        new ReportCellFormatter()
            .FormatDisplay(new DateTime(2026, 3, 14, 13, 2, 3, DateTimeKind.Utc), grain)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "03/14/2026 01:02:03 PM")]
    [InlineData(ReportTimeGrain.Day, "03/14/2026")]
    [InlineData(ReportTimeGrain.Week, "Week of 03/14/2026")]
    [InlineData(ReportTimeGrain.Month, "March 2026")]
    [InlineData(ReportTimeGrain.Quarter, "Q1 2026")]
    [InlineData(ReportTimeGrain.Year, "2026")]
    public void FormatDisplay_DateTimeOffset_CoversEveryTimeGrain(ReportTimeGrain? grain, string expected)
    {
        new ReportCellFormatter()
            .FormatDisplay(new DateTimeOffset(2026, 3, 14, 13, 2, 3, TimeSpan.Zero), grain)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("period")]
    [InlineData("period__")]
    [InlineData("period__unknown")]
    public void BuildCell_ColumnWithoutKnownTimeSuffix_UsesDefaultDateFormat(string? columnCode)
    {
        var cell = new ReportCellFormatter().BuildCell(
            new DateOnly(2026, 3, 14),
            new ReportSheetColumnDto(columnCode!, "Period", "date"));

        cell.Display.Should().Be("03/14/2026");
    }

    [Theory]
    [InlineData("period_utc__day", ReportTimeGrain.Day, "03/14/2026")]
    [InlineData("period_utc__week", ReportTimeGrain.Week, "Week of 03/14/2026")]
    [InlineData("period_utc__month", ReportTimeGrain.Month, "March 2026")]
    [InlineData("period_utc__quarter", ReportTimeGrain.Quarter, "Q1 2026")]
    [InlineData("period_utc__year", ReportTimeGrain.Year, "2026")]
    public void BuildCell_TimeGrained_Period_Uses_UserFacing_Display(string columnCode, ReportTimeGrain timeGrain, string expected)
    {
        var sut = new ReportCellFormatter();
        var column = new ReportSheetColumnDto(columnCode, "Period", "datetime", SemanticRole: "row-group");

        var cell = sut.BuildCell(new DateOnly(2026, 3, 14), column);

        cell.Display.Should().Be(expected);
        sut.FormatGroupLabel(new DateOnly(2026, 3, 14), timeGrain).Should().Be(expected);
    }
}
