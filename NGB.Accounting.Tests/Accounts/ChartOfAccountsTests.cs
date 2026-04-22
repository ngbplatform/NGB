using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Accounting.Tests.Accounts;

public sealed class ChartOfAccountsTests
{
    [Fact]
    public void Add_Null_Throws()
    {
        // Arrange
        var chart = new ChartOfAccounts();

        // Act
        var act = () => chart.Add(null!);

        // Assert
        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Add_DuplicateId_Throws()
    {
        // Arrange
        var chart = new ChartOfAccounts();
        var id = Guid.CreateVersion7();

        var a1 = new Account(
            id: id,
            code: "41",
            name: "Inventory",
            type: AccountType.Asset);

        var a2 = new Account(
            id: id,
            code: "42",
            name: "Other",
            type: AccountType.Asset);

        chart.Add(a1);

        // Act
        var act = () => chart.Add(a2);

        // Assert
        var ex = act.Should().Throw<AccountAlreadyExistsException>()
            .Which;

        ex.ErrorCode.Should().Be(AccountAlreadyExistsException.ErrorCodeConst);
        ex.Context.Should().ContainKeys("accountId", "attemptedAccountId", "code", "codeNorm");
        ex.Context["accountId"].Should().Be(id);
        ex.Context["code"].Should().Be("41");
        ex.Context["codeNorm"].Should().Be("41");
    }

    [Fact]
    public void Add_DuplicateCode_AfterNormalize_Throws()
    {
        // Arrange
        var chart = new ChartOfAccounts();

        var a1 = new Account(
            id: Guid.CreateVersion7(),
            code: " 41 ",
            name: "Inventory",
            type: AccountType.Asset);

        var a2 = new Account(
            id: Guid.CreateVersion7(),
            code: "41",
            name: "Inventory2",
            type: AccountType.Asset);

        chart.Add(a1);

        // Act
        var act = () => chart.Add(a2);

        // Assert
        var ex = act.Should().Throw<AccountAlreadyExistsException>()
            .Which;

        ex.ErrorCode.Should().Be(AccountAlreadyExistsException.ErrorCodeConst);
        ex.Context.Should().ContainKeys("accountId", "attemptedAccountId", "code", "codeNorm");
        ex.Context["accountId"].Should().Be(a1.Id);
        ex.Context["attemptedAccountId"].Should().Be(a2.Id);
        ex.Context["code"].Should().Be("41");
        ex.Context["codeNorm"].Should().Be("41");
    }

    [Fact]
    public void Get_ById_ReturnsSameInstance()
    {
        // Arrange
        var chart = new ChartOfAccounts();
        var a1 = new Account(
            id: Guid.CreateVersion7(),
            code: "41",
            name: "Inventory",
            type: AccountType.Asset);
        chart.Add(a1);

        // Act
        var got = chart.Get(a1.Id);

        // Assert
        got.Should().BeSameAs(a1);
    }

    [Fact]
    public void Get_ByCode_IsCaseAndWhitespaceInsensitive()
    {
        // Arrange
        var chart = new ChartOfAccounts();
        var a1 = new Account(
            id: Guid.CreateVersion7(),
            code: "41",
            name: "Inventory",
            type: AccountType.Asset);
        chart.Add(a1);

        // Act
        var got = chart.Get("  41  ");

        // Assert
        got.Should().BeSameAs(a1);
    }

    [Fact]
    public void TryGetByCode_Missing_ReturnsFalse()
    {
        // Arrange
        var chart = new ChartOfAccounts();

        // Act
        var ok = chart.TryGetByCode("999", out var _);

        // Assert
        ok.Should().BeFalse();
    }
}
