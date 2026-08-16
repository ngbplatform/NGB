using FluentAssertions;
using NGB.Accounting.CashFlow;
using Xunit;

namespace NGB.Accounting.Tests.CashFlow;

public sealed class CashFlowRoleRulesTests
{
    [Theory]
    [InlineData(CashFlowRole.WorkingCapital, true, false, true)]
    [InlineData(CashFlowRole.NonCashOperatingAdjustment, true, false, true)]
    [InlineData(CashFlowRole.InvestingCounterparty, true, false, true)]
    [InlineData(CashFlowRole.FinancingCounterparty, true, false, true)]
    [InlineData(CashFlowRole.None, false, true, false)]
    [InlineData(CashFlowRole.CashEquivalent, false, true, false)]
    public void LineCodeRules_AllRoleCategories_ReturnExpected(
        CashFlowRole role,
        bool requires,
        bool forbids,
        bool supports)
    {
        CashFlowRoleRules.RequiresLineCode(role).Should().Be(requires);
        CashFlowRoleRules.ForbidsLineCode(role).Should().Be(forbids);
        CashFlowRoleRules.SupportsLineCode(role).Should().Be(supports);
    }

    [Theory]
    [InlineData(CashFlowSection.Operating, "op_wc_inventory", CashFlowRole.WorkingCapital)]
    [InlineData(CashFlowSection.Operating, "op_depreciation", CashFlowRole.NonCashOperatingAdjustment)]
    [InlineData(CashFlowSection.Investing, "inv_equipment", CashFlowRole.InvestingCounterparty)]
    [InlineData(CashFlowSection.Financing, "fin_loan", CashFlowRole.FinancingCounterparty)]
    public void GetAllowedRoles_SupportedSection_ReturnsExpected(
        CashFlowSection section,
        string lineCode,
        CashFlowRole expected)
    {
        var line = CreateLine(lineCode, section);

        CashFlowRoleRules.GetAllowedRoles(line).Should().Equal(expected);
    }

    [Fact]
    public void GetAllowedRoles_UnsupportedSection_ReturnsEmpty()
    {
        var line = CreateLine("reconciliation", CashFlowSection.Reconciliation);

        var result = CashFlowRoleRules.GetAllowedRoles(line);

        result.Should().BeEmpty();
    }

    private static CashFlowLineDefinition CreateLine(string lineCode, CashFlowSection section)
        => new(lineCode, CashFlowMethod.Indirect, section, "Line", 10, IsSystem: true);
}
