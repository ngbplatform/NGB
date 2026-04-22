using FluentAssertions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Tools.Tests.Exceptions;

/// <summary>
/// P0: Argument/validation exceptions are used heavily across the platform.
/// Their codes/context must remain stable.
/// </summary>
public sealed class NgbArgumentExceptions_Contracts_P0Tests
{
    [Fact]
    public void Required_StoresParamName_AndStableCode()
    {
        var ex = new NgbArgumentRequiredException("arg");

        ex.Kind.Should().Be(NgbErrorKind.Validation);
        ex.ErrorCode.Should().Be(NgbArgumentRequiredException.Code);
        ex.ParamName.Should().Be("arg");
        ex.Context.Should().ContainKey("paramName").WhoseValue.Should().Be("arg");
        ex.Message.Should().Be("Arg is required.");
    }

    [Fact]
    public void Invalid_StoresParamNameReason_AndStableCode()
    {
        var ex = new NgbArgumentInvalidException("arg", "bad value");

        ex.Kind.Should().Be(NgbErrorKind.Validation);
        ex.ErrorCode.Should().Be(NgbArgumentInvalidException.Code);
        ex.ParamName.Should().Be("arg");
        ex.Reason.Should().Be("bad value");
        ex.Context.Should().ContainKey("paramName").WhoseValue.Should().Be("arg");
        ex.Context.Should().ContainKey("reason").WhoseValue.Should().Be("bad value");
        ex.Message.Should().Be("bad value");
    }

    [Fact]
    public void OutOfRange_StoresParamNameActualValueReason_AndStableCode()
    {
        var ex = new NgbArgumentOutOfRangeException("count", actualValue: 10, reason: "must be <= 3");

        ex.Kind.Should().Be(NgbErrorKind.Validation);
        ex.ErrorCode.Should().Be(NgbArgumentOutOfRangeException.Code);
        ex.ParamName.Should().Be("count");
        ex.ActualValue.Should().Be(10);
        ex.Context.Should().ContainKey("paramName").WhoseValue.Should().Be("count");
        ex.Context.Should().ContainKey("actualValue").WhoseValue.Should().Be(10);
        ex.Context.Should().ContainKey("reason").WhoseValue.Should().Be("must be <= 3");
        ex.Message.Should().Be("Count is out of range. Must be <= 3");
    }

    [Fact]
    public void OutOfRange_WhenParamNameIsBlank_ThrowsRequired()
    {
        Action act = () => _ = new NgbArgumentOutOfRangeException(" ", actualValue: 1, reason: "bad");

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("paramName");
    }
}
