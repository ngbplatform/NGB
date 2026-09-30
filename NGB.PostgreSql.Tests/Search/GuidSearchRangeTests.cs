using FluentAssertions;
using NGB.PostgreSql.Search;
using Xunit;

namespace NGB.PostgreSql.Tests.Search;

public sealed class GuidSearchRangeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01a0493")]
    [InlineData("01a0493g")]
    [InlineData("01a049390")]
    [InlineData("01a04939_e")]
    [InlineData("01a04939-ea8d-767e-a7ad-8d9d2facfa2b0")]
    public void TryCreate_rejects_unbounded_or_noncanonical_fragments(string? value)
    {
        GuidSearchRange.TryCreate(value, out var range).Should().BeFalse();
        range.Should().Be(default(GuidSearchRange));
    }

    [Theory]
    [InlineData("01a04939", "01a04939-0000-0000-0000-000000000000", "01a04939-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("01A04939-", "01a04939-0000-0000-0000-000000000000", "01a04939-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("01a04939-ea8d", "01a04939-ea8d-0000-0000-000000000000", "01a04939-ea8d-ffff-ffff-ffffffffffff")]
    public void TryCreate_builds_inclusive_uuid_bounds(string value, string lower, string upper)
    {
        GuidSearchRange.TryCreate(value, out var range).Should().BeTrue();
        range.Lower.Should().Be(Guid.Parse(lower));
        range.Upper.Should().Be(Guid.Parse(upper));
    }

    [Fact]
    public void TryCreate_for_complete_canonical_uuid_builds_a_single_value_range()
    {
        const string value = "01a04939-ea8d-767e-a7ad-8d9d2facfa2b";

        GuidSearchRange.TryCreate(value, out var range).Should().BeTrue();

        range.Lower.Should().Be(Guid.Parse(value));
        range.Upper.Should().Be(range.Lower);
    }
}
