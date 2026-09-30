using FluentAssertions;
using NGB.Accounting.Turnovers;
using Xunit;

namespace NGB.Accounting.Tests.Turnovers;

public sealed class AccountingTurnoverCalculatorTests
{
    [Fact]
    public void Calculate_EmptyEntries_ReturnsEmpty()
    {
        var result = new AccountingTurnoverCalculator().Calculate([]);

        result.Should().BeEmpty();
    }
}
