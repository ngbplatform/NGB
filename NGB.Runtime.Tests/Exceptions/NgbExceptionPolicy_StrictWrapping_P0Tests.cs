using System.Data.Common;
using FluentAssertions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Exceptions;

/// <summary>
/// P0: The exception policy is a safety net and must not leak raw BCL exceptions.
/// Custom exceptions are expected to be thrown at call sites; the policy wraps everything else.
/// </summary>
public sealed class NgbExceptionPolicy_StrictWrapping_P0Tests
{
    [Fact]
    public void Apply_WhenExceptionIsNull_ThrowsRequired()
    {
        Action act = () => NgbExceptionPolicy.Apply(null!, "it.op");

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.Context.Should().ContainKey("paramName").WhoseValue.Should().Be("ex");
    }

    [Fact]
    public void Apply_WhenOperationCanceled_PassesThrough()
    {
        var ex = new OperationCanceledException();

        var result = NgbExceptionPolicy.Apply(ex, "it.op");

        result.Should().BeSameAs(ex);
    }

    [Fact]
    public void Apply_WhenTimeoutException_WrapsIntoTimeout()
    {
        var ex = new TimeoutException("boom");

        var result = NgbExceptionPolicy.Apply(ex, "it.op");

        result.Should().BeOfType<NgbTimeoutException>();
        var wrapped = (NgbTimeoutException)result;
        wrapped.InnerException.Should().BeSameAs(ex);
        wrapped.Operation.Should().Be("it.op");
        wrapped.ErrorCode.Should().Be(NgbTimeoutException.Code);
        wrapped.Context.Should().ContainKey("operation").WhoseValue.Should().Be("it.op");
        wrapped.Context.Should().ContainKey("exceptionType");
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
    public void Apply_WhenBclException_WrapsIntoUnexpected()
    {
        var ex = new NotSupportedException("boom");

        var result = NgbExceptionPolicy.Apply(ex, "it.op");

        result.Should().BeOfType<NgbUnexpectedException>();
        var wrapped = (NgbUnexpectedException)result;
        wrapped.InnerException.Should().BeSameAs(ex);
        wrapped.Operation.Should().Be("it.op");
        wrapped.Context.Should().ContainKey("operation").WhoseValue.Should().Be("it.op");
        wrapped.Context.Should().ContainKey("exceptionType");
    }

    [Fact]
    public void Apply_WhenNonNgbException_WrapsIntoUnexpected()
    {
        var ex = new FormatException("bad");

        var result = NgbExceptionPolicy.Apply(ex, "it.op");

        result.Should().BeOfType<NgbUnexpectedException>();
        var wrapped = (NgbUnexpectedException)result;
        wrapped.InnerException.Should().BeSameAs(ex);
        wrapped.Operation.Should().Be("it.op");
    }

    private sealed class FakeDbException : DbException
    {
        public FakeDbException() : base("db") { }
    }
}
