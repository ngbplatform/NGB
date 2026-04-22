using FluentAssertions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Tools.Tests.Exceptions;

/// <summary>
/// P0: The base exception contract is relied upon for stable error handling and logging.
/// </summary>
public sealed class NgbException_Contract_P0Tests
{
    [Fact]
    public void Ctor_EnrichesExceptionData_WithContractFields_AndContext()
    {
        var ex = new FakeException(
            message: "boom",
            errorCode: "it.code",
            kind: NgbErrorKind.Conflict,
            context: new Dictionary<string, object?>
            {
                ["k1"] = "v1",
                ["k2"] = 123
            });

        ex.ErrorCode.Should().Be("it.code");
        ex.Kind.Should().Be(NgbErrorKind.Conflict);
        ex.Context.Should().ContainKey("k1").WhoseValue.Should().Be("v1");

		ex.Data.Contains("ngb.error_code").Should().BeTrue();
		ex.Data["ngb.error_code"].Should().Be("it.code");

		ex.Data.Contains("ngb.kind").Should().BeTrue();
		ex.Data["ngb.kind"].Should().Be("Conflict");

		ex.Data.Contains("ngb.ctx.k1").Should().BeTrue();
		ex.Data["ngb.ctx.k1"].Should().Be("v1");

		ex.Data.Contains("ngb.ctx.k2").Should().BeTrue();
		ex.Data["ngb.ctx.k2"].Should().Be(123);
    }

    [Fact]
    public void ToString_ContainsContractFields_AndSerializesContext()
    {
        var ex = new FakeException(
            message: "boom",
            errorCode: "it.code",
            kind: NgbErrorKind.Validation,
            context: new Dictionary<string, object?> { ["x"] = "y" });

        var s = ex.ToString();

        s.Should().Contain("ErrorCode: it.code");
        s.Should().Contain("Kind: Validation");
        s.Should().Contain("Context:");
        s.Should().Contain("\"x\"");
        s.Should().Contain("\"y\"");
    }

    [Fact]
    public void ToString_WhenContextSerializationFails_DoesNotThrow_AndProvidesFallback()
    {
        var cycle = new Cycle();
        cycle.Self = cycle;

        var ex = new FakeException(
            message: "boom",
            errorCode: "it.code",
            kind: NgbErrorKind.Infrastructure,
            context: new Dictionary<string, object?> { ["cycle"] = cycle });

        Action act = () => _ = ex.ToString();

        act.Should().NotThrow();
        ex.ToString().Should().Contain("Context: {count:1}");
    }

    private sealed class FakeException : NgbException
    {
        public FakeException(
            string message,
            string errorCode,
            NgbErrorKind kind,
            IReadOnlyDictionary<string, object?>? context = null,
            Exception? inner = null)
            : base(message, errorCode, kind, context, inner) { }
    }

    private sealed class Cycle
    {
        public Cycle? Self { get; set; }
    }
}
