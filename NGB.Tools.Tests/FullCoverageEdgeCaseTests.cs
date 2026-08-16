using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Tools.Normalization;
using Xunit;

namespace NGB.Tools.Tests;

public sealed class FullCoverageEdgeCaseTests
{
    [Theory]
    [InlineData(null, "Value")]
    [InlineData("   ", "Value")]
    [InlineData("...", "Value")]
    [InlineData("request_payload_fields_parameters_filters_layout_customerIdUtcInclusive", "Customer")]
    [InlineData("request.customer-name[]", "Customer Name")]
    [InlineData("a1B", "A1 B")]
    [InlineData("HTTPServer", "Httpserver")]
    [InlineData("x", "X")]
    public void ArgumentLabelFormatter_AllTokenShapes_ReturnExpected(string? value, string expected)
    {
        NgbArgumentLabelFormatter.Format(value).Should().Be(expected);
    }

    [Fact]
    public void OutOfRange_BlankReason_UsesDefaultMessage()
    {
        var exception = new NgbArgumentOutOfRangeException("itemCount", 0, "  ");

        exception.Message.Should().Be("Item Count is out of range.");
    }

    [Fact]
    public void OutOfRange_UppercaseReason_PreservesCapitalization()
    {
        var exception = new NgbArgumentOutOfRangeException("itemCount", 0, "Already capitalized.");

        exception.Message.Should().EndWith("Already capitalized.");
    }

    [Fact]
    public void NgbException_ContextEnumerationFailure_IsSwallowedByConstructor()
    {
        var context = new ThrowingReadOnlyDictionary();

        var act = () => new TestNgbException(context);

        act.Should().NotThrow();
        new TestNgbException(context).Context.Should().BeSameAs(context);
    }

    [Fact]
    public void TimeoutAndUnexpected_NullAdditionalContext_UseSafeOperationAndType()
    {
        var inner = new TimeoutException("secret");

        var timeout = new NgbTimeoutException(" ", inner);
        var unexpected = new NgbUnexpectedException(" ", inner);

        timeout.Operation.Should().Be("(unknown)");
        timeout.ExceptionType.Should().Be(typeof(TimeoutException).ToString());
        timeout.Context["exceptionType"].Should().Be(typeof(TimeoutException).ToString());
        unexpected.Operation.Should().Be(" ");
        unexpected.ExceptionType.Should().Be(typeof(TimeoutException).ToString());
        unexpected.Context["operation"].Should().Be("(unknown)");
    }

    [Fact]
    public void EnumExtensions_DefinedUndefinedAndDisplayFallbacks_ReturnExpected()
    {
        EnumExtensions.GetAttribute<DisplaySample, DisplayAttribute>((DisplaySample)999).Should().BeNull();
        EnumExtensions.GetAttribute<DisplaySample, DisplayAttribute>(DisplaySample.Decorated)!.Name.Should().Be("Friendly");
        DisplaySample.Decorated.ToDisplay().Should().Be("Friendly");
        DisplaySample.EmptyDisplay.ToDisplay().Should().Be(nameof(DisplaySample.EmptyDisplay));
        DisplaySample.Plain.ToDisplay().Should().Be(nameof(DisplaySample.Plain));
        DisplaySample.Plain.ToCode().Should().Be(nameof(DisplaySample.Plain));
    }

    [Fact]
    public void EnsureNonEmpty_BlankName_ThrowsRequired()
    {
        var act = () => Guid.CreateVersion7().EnsureNonEmpty(" ");

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void ParseGuidOrRef_AllSupportedShapes_ReturnExpected()
    {
        var id = Guid.CreateVersion7();

        ParseJson($"\"{id}\"").ParseGuidOrRef().Should().Be(id);
        ParseJson($"{{\"id\":\"{id}\"}}").ParseGuidOrRef().Should().Be(id);
        ParseJson($"{{\"Id\":\"{id}\"}}").ParseGuidOrRef().Should().Be(id);
    }

    [Fact]
    public void ParseGuidOrRef_NullOrUnsupportedShapes_ThrowStableExceptions()
    {
        var nullId = () => ParseJson("{\"id\":null}").ParseGuidOrRef();
        var numericId = () => ParseJson("{\"id\":123}").ParseGuidOrRef();
        var missingId = () => ParseJson("{}").ParseGuidOrRef();
        var booleanValue = () => ParseJson("true").ParseGuidOrRef();

        nullId.Should().Throw<NgbArgumentInvalidException>();
        numericId.Should().Throw<FormatException>();
        missingId.Should().Throw<FormatException>();
        booleanValue.Should().Throw<FormatException>();
    }

    [Fact]
    public void TimeProviderExtensions_NullAndBoundaryDate_ReturnExpected()
    {
        TimeProvider nullProvider = null!;
        var nullAct = () => nullProvider.GetUtcNowDateTime();
        var provider = new FixedTimeProvider(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero));

        nullAct.Should().Throw<NgbArgumentRequiredException>();
        provider.GetUtcNowDateTime().Should().Be(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        provider.GetUtcToday().Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public void NormalizeStrictToken_DigitAndLetterBranches_AreBothSupported()
    {
        IdentifierNormalization.NormalizeStrictToken("A1-Z", "code", "empty").Should().Be("a1_z");
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private enum DisplaySample
    {
        [Display(Name = "Friendly")]
        Decorated,

        [Display]
        EmptyDisplay,

        Plain
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TestNgbException(IReadOnlyDictionary<string, object?> context)
        : NgbException("test", "test.code", NgbErrorKind.Unknown, context);

    private sealed class ThrowingReadOnlyDictionary : IReadOnlyDictionary<string, object?>
    {
        public int Count => 1;
        public IEnumerable<string> Keys => ["key"];
        public IEnumerable<object?> Values => ["value"];
        public object? this[string key] => "value";
        public bool ContainsKey(string key) => key == "key";
        public bool TryGetValue(string key, out object? value)
        {
            value = "value";
            return key == "key";
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => throw new InvalidOperationException("boom");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
