using FluentAssertions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public static class NgbExceptionAssertions
{
    public static void AssertNgbError(this NgbException ex, string errorCode, params string[] requiredContextKeys)
    {
        ex.ErrorCode.Should().Be(errorCode);

        foreach (var key in requiredContextKeys)
            ex.Context.Should().ContainKey(key);
    }

    public static void AssertReason(this NgbException ex, string expectedReason)
    {
        ex.Context.Should().ContainKey("reason");
        ex.Context["reason"].Should().Be(expectedReason);
    }

    public static void AssertReasonContains(this NgbException ex, string expectedSubstring)
    {
        ex.Context.Should().ContainKey("reason");

        ex.Context["reason"]
            .Should()
            .BeOfType<string>()
            .Which
            .Should()
            .Contain(expectedSubstring);
    }
}
