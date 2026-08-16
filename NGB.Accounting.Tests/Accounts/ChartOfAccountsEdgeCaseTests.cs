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
    public void TryGet_WhitespaceCode_ReturnsFalseAndNull()
    {
        var chart = new ChartOfAccounts();

        var found = chart.TryGet(" \t ", out var account);

        found.Should().BeFalse();
        account.Should().BeNull();
    }
}
