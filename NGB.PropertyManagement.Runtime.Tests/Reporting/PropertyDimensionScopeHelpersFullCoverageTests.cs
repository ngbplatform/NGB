using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NGB.Persistence.Catalogs.Universal;
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

    [Fact]
    public void Property_field_guid_reader_covers_every_supported_and_invalid_runtime_shape()
    {
        var id = Guid.CreateVersion7();
        AssertGuid(new Dictionary<string, object?>(), false, Guid.Empty);
        AssertGuid(new Dictionary<string, object?> { ["field"] = null }, false, Guid.Empty);
        AssertGuid(new Dictionary<string, object?> { ["field"] = id }, true, id);
        AssertGuid(new Dictionary<string, object?> { ["field"] = Guid.Empty }, false, Guid.Empty);
        AssertGuid(new Dictionary<string, object?> { ["field"] = id.ToString() }, true, id);
        AssertGuid(new Dictionary<string, object?> { ["field"] = "invalid" }, false, Guid.Empty);
        AssertGuid(new Dictionary<string, object?> { ["field"] = Guid.Empty.ToString() }, false, Guid.Empty);
        AssertGuid(new Dictionary<string, object?> { ["field"] = JsonSerializer.SerializeToElement(id.ToString()) }, true, id);
        AssertGuid(new Dictionary<string, object?> { ["field"] = JsonSerializer.SerializeToElement("invalid") }, false, Guid.Empty);
        AssertGuid(new Dictionary<string, object?> { ["field"] = JsonSerializer.SerializeToElement(Guid.Empty.ToString()) }, false, Guid.Empty);
        AssertGuid(new Dictionary<string, object?> { ["field"] = 42 }, false, Guid.Empty);
    }

    [Fact]
    public void Hierarchy_snapshot_adds_building_descendants_and_skips_duplicates_missing_rows_and_units()
    {
        var root = Guid.CreateVersion7();
        var building = Guid.CreateVersion7();
        var unit = Guid.CreateVersion7();
        var grandchild = Guid.CreateVersion7();
        var leafBuilding = Guid.CreateVersion7();
        var missing = Guid.CreateVersion7();
        var duplicate = Guid.CreateVersion7();
        var rows = new Dictionary<Guid, CatalogHeadRow>
        {
            [building] = Row(building, "Building"),
            [unit] = Row(unit, "Unit"),
            [grandchild] = Row(grandchild, "Unit"),
            [leafBuilding] = Row(leafBuilding, "building"),
            [duplicate] = Row(duplicate, "Building")
        };
        var children = new Dictionary<Guid, List<Guid>>
        {
            [root] = [duplicate, missing, building, leafBuilding, unit],
            [building] = [grandchild]
        };
        var ids = new SortedSet<Guid> { root, duplicate };

        var nestedType = typeof(PropertyManagementPropertyDimensionScopeExpander)
            .GetNestedType("PropertyHierarchySnapshot", BindingFlags.NonPublic)!;
        var snapshot = Activator.CreateInstance(
            nestedType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [rows, children],
            culture: null)!;
        nestedType.GetMethod("AddDescendants", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(snapshot, [root, ids]);

        ids.Should().BeEquivalentTo([root, duplicate, missing, building, leafBuilding, unit, grandchild]);
        nestedType.GetProperty("RowsById")!.GetValue(snapshot).Should().BeSameAs(rows);
    }

    private static object? InvokeStatic(string method, params object?[] args)
        => typeof(PropertyManagementPropertyDimensionScopeExpander)
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, args);

    private static string? ReadString(IReadOnlyDictionary<string, object?> fields)
        => (string?)InvokeStatic("ReadString", fields, "field");

    private static void AssertGuid(
        IReadOnlyDictionary<string, object?> fields,
        bool expectedResult,
        Guid expectedValue)
    {
        object?[] args = [fields, "field", null];
        InvokeStatic("TryReadGuid", args).Should().Be(expectedResult);
        args[2].Should().Be(expectedValue);
    }

    private static CatalogHeadRow Row(Guid id, object? kind)
        => new(id, IsMarkedForDeletion: false, id.ToString(), new Dictionary<string, object?> { ["kind"] = kind });
}
