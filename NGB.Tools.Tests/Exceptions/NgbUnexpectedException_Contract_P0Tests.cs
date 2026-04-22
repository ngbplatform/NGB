using FluentAssertions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Tools.Tests.Exceptions;

/// <summary>
/// P0: Unexpected surface must be stable and must not leak raw exception messages into Context.
/// </summary>
public sealed class NgbUnexpectedException_Contract_P0Tests
{
    [Fact]
    public void Ctor_BuildsStableContext_AndPreservesInnerException()
    {
        var inner = new InvalidOperationException("boom (should not leak)");
        var ex = new NgbUnexpectedException(
            operation: "it.op",
            innerException: inner,
            additionalContext: new Dictionary<string, object?> { ["x"] = "y" });

        ex.Kind.Should().Be(NgbErrorKind.Infrastructure);
        ex.ErrorCode.Should().Be(NgbUnexpectedException.Code);
        ex.Message.Should().Be("Unexpected internal error.");
        ex.InnerException.Should().BeSameAs(inner);

        ex.Context.Should().ContainKey("x").WhoseValue.Should().Be("y");
        ex.Context.Should().ContainKey("operation").WhoseValue.Should().Be("it.op");
        ex.Context.Should().ContainKey("exceptionType");

        ex.Context.Values.Select(v => v?.ToString() ?? string.Empty)
            .Should().NotContain(v => v.Contains("should not leak", StringComparison.Ordinal));
    }

    [Fact]
    public void Ctor_WhenOperationIsBlank_UsesUnknownOperation_InContext_ButKeepsOriginalOperationProperty()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new NgbUnexpectedException(operation: " ", innerException: inner);

        // Property is not normalized by design.
        ex.Operation.Should().Be(" ");
        ex.Context.Should().ContainKey("operation").WhoseValue.Should().Be("(unknown)");
    }
}
