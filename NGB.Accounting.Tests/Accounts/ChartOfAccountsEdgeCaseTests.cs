using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Accounting.Tests.Accounts;

public sealed class ChartOfAccountsEdgeCaseTests
{
    [Fact]
    public void Get_WhitespaceCode_Throws()
    {
        var chart = new ChartOfAccounts();

        var act = () => chart.Get("  ");

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Get_MissingId_Throws()
    {
        var accountId = Guid.CreateVersion7();
        var chart = new ChartOfAccounts();

        var act = () => chart.Get(accountId);

        var exception = act.Should().Throw<AccountNotFoundException>().Which;
        exception.Context["accountId"].Should().Be(accountId);
    }

    [Fact]
    public void Account_not_found_by_code_preserves_code_and_structured_context()
    {
        var exception = new AccountNotFoundException(" 1000 ");

        exception.AccountId.Should().BeNull();
        exception.Code.Should().Be(" 1000 ");
        exception.ErrorCode.Should().Be(AccountNotFoundException.ErrorCodeConst);
        exception.Context.Should().ContainKey("code").WhoseValue.Should().Be(" 1000 ");
    }

    [Fact]
    public void TryGet_WhitespaceCode_ReturnsFalseAndNull()
    {
        var chart = new ChartOfAccounts();

        var found = chart.TryGet(" \t ", out var account);

        found.Should().BeFalse();
        account.Should().BeNull();
    }
}
