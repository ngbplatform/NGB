using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NGB.PropertyManagement.Runtime.Reporting;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Reporting;

public sealed class PropertyDimensionScopeHelpersFullCoverageTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData("building", "Building")]
    [InlineData("UNIT", "Unit")]
    [InlineData(" custom ", "custom")]
    public void Property_kind_normalization_covers_known_blank_and_custom_values(string? raw, string? expected)
        => InvokeStatic("NormalizeKind", raw).Should().Be(expected);

    [Fact]
    public void Property_field_string_reader_covers_every_supported_runtime_shape()
    {
        ReadString(new Dictionary<string, object?>()).Should().BeNull();
        ReadString(new Dictionary<string, object?> { ["field"] = null }).Should().BeNull();
        ReadString(new Dictionary<string, object?> { ["field"] = "value" }).Should().Be("value");
        ReadString(new Dictionary<string, object?> { ["field"] = JsonSerializer.SerializeToElement("json") }).Should().Be("json");
        ReadString(new Dictionary<string, object?> { ["field"] = JsonSerializer.SerializeToElement(42) }).Should().Be("42");
        ReadString(new Dictionary<string, object?> { ["field"] = 17 }).Should().Be("17");
    }

    private static object? InvokeStatic(string method, params object?[] args)
        => typeof(PropertyManagementPropertyDimensionScopeExpander)
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, args);

    private static string? ReadString(IReadOnlyDictionary<string, object?> fields)
        => (string?)InvokeStatic("ReadString", fields, "field");
}
