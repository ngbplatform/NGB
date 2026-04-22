using System.Data.Common;
using FluentAssertions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Tools.Tests.Exceptions;

/// <summary>
/// P0: Exception policy is a boundary safety net and must keep stable behavior.
/// </summary>
public sealed class NgbExceptionPolicy_Wrapping_P0Tests
{
    [Fact]
    public void Apply_WhenExceptionIsNull_ThrowsRequired()
    {
        Action act = () => _ = NgbExceptionPolicy.Apply(null!, "it.op");

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("ex");
    }

    [Fact]
    public void Apply_WhenOperationCanceled_PassesThrough()
    {
        var ex = new OperationCanceledException();

        var result = NgbExceptionPolicy.Apply(ex, "it.op");

        result.Should().BeSameAs(ex);
    }

    [Fact]
    public void Apply_WhenAlreadyNgbException_PassesThrough()
    {
        var ex = new NgbArgumentRequiredException("x");

        var result = NgbExceptionPolicy.Apply(ex, "it.op");

        result.Should().BeSameAs(ex);
    }

    [Fact]
    public void Apply_WhenDbException_PassesThrough()
    {
        var ex = new FakeDbException();

        var result = NgbExceptionPolicy.Apply(ex, "it.op");

        result.Should().BeSameAs(ex);
    }

    [Fact]
    public void Apply_WhenTimeoutException_WrapsIntoTimeout_AndAddsContext()
    {
        var ex = new TimeoutException("boom (should not leak)");

        var result = NgbExceptionPolicy.Apply(ex, "it.op", new Dictionary<string, object?>
        {
            ["x"] = "y",
            ["operation"] = "should be overwritten"
        });

        result.Should().BeOfType<NgbTimeoutException>();
        var wrapped = (NgbTimeoutException)result;
        wrapped.InnerException.Should().BeSameAs(ex);
        wrapped.Operation.Should().Be("it.op");
        wrapped.Context.Should().ContainKey("x").WhoseValue.Should().Be("y");
        wrapped.Context.Should().ContainKey("operation").WhoseValue.Should().Be("it.op");

        wrapped.Context.Values.Select(v => v?.ToString() ?? string.Empty)
            .Should().NotContain(v => v.Contains("should not leak", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_WhenOtherException_WrapsIntoUnexpected_AndPreservesInnerException()
    {
        var ex = new NotSupportedException("boom (should not leak)");

        var result = NgbExceptionPolicy.Apply(ex, "it.op", new Dictionary<string, object?> { ["x"] = "y" });

        result.Should().BeOfType<NgbUnexpectedException>();
        var wrapped = (NgbUnexpectedException)result;
        wrapped.InnerException.Should().BeSameAs(ex);
        wrapped.Context.Should().ContainKey("x").WhoseValue.Should().Be("y");
        wrapped.Context.Should().ContainKey("operation").WhoseValue.Should().Be("it.op");
        wrapped.Context.Values.Select(v => v?.ToString() ?? string.Empty)
            .Should().NotContain(v => v.Contains("should not leak", StringComparison.Ordinal));
    }

    private sealed class FakeDbException : DbException
    {
        public FakeDbException() : base("db") { }
    }
}
