using FluentAssertions;
using NGB.Accounting.Dimensions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Accounting.Tests.Dimensions;

public sealed class AccountDimensionRuleTests
{
    [Fact]
    public void Constructor_EmptyDimensionId_Throws()
    {
        var act = () => new AccountDimensionRule(Guid.Empty, "warehouse", 10, true);

        act.Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void Constructor_WhitespaceDimensionCode_Throws()
    {
        var act = () => new AccountDimensionRule(Guid.CreateVersion7(), "  ", 10, true);

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveOrdinal_Throws(int ordinal)
    {
        var act = () => new AccountDimensionRule(Guid.CreateVersion7(), "warehouse", ordinal, true);

        act.Should().Throw<NgbArgumentOutOfRangeException>();
    }
}
