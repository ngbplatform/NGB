using FluentAssertions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Tools.Tests.Exceptions;

/// <summary>
/// P0: Timeout surface must be stable and must not leak raw exception messages into Context.
/// </summary>
public sealed class NgbTimeoutException_Contract_P0Tests
{
    [Fact]
    public void Ctor_BuildsStableContext_AndPreservesInnerException()
    {
        var inner = new TimeoutException("boom (should not leak)");
        var ex = new NgbTimeoutException(
            operation: "it.op",
            innerException: inner,
            additionalContext: new Dictionary<string, object?> { ["x"] = "y" });

        ex.Kind.Should().Be(NgbErrorKind.Infrastructure);
        ex.ErrorCode.Should().Be(NgbTimeoutException.Code);
        ex.Message.Should().Be("Operation timed out.");
        ex.InnerException.Should().BeSameAs(inner);

        ex.Operation.Should().Be("it.op");
        ex.ExceptionType.Should().Contain("TimeoutException");

        ex.Context.Should().ContainKey("x").WhoseValue.Should().Be("y");
        ex.Context.Should().ContainKey("operation").WhoseValue.Should().Be("it.op");
        ex.Context.Should().ContainKey("exceptionType");

        ex.Context.Values.Select(v => v?.ToString() ?? string.Empty)
            .Should().NotContain(v => v.Contains("should not leak", StringComparison.Ordinal));
    }

    [Fact]
    public void Ctor_WhenOperationIsBlank_UsesUnknownOperation()
    {
        var inner = new TimeoutException("boom");
        var ex = new NgbTimeoutException(operation: "  ", innerException: inner);

        ex.Operation.Should().Be("(unknown)");
        ex.Context.Should().ContainKey("operation").WhoseValue.Should().Be("(unknown)");
    }
}
