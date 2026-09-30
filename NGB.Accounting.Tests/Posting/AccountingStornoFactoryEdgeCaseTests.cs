using FluentAssertions;
using NGB.Accounting.Posting;
using Xunit;

namespace NGB.Accounting.Tests.Posting;

public sealed class AccountingStornoFactoryEdgeCaseTests
{
    [Fact]
    public void Create_EmptyEntries_ReturnsEmpty()
    {
        var result = AccountingStornoFactory.Create([]);

        result.Should().BeEmpty();
    }
}
