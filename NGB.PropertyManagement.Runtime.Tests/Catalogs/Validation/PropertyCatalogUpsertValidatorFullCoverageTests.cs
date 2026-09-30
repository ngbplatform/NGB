using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Core.Catalogs;
using NGB.Definitions.Catalogs.Validation;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Universal;
using NGB.PropertyManagement.Runtime.Catalogs.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Catalogs.Validation;

public sealed class PropertyCatalogUpsertValidatorFullCoverageTests
{
    [Fact]
    public async Task Binding_kind_and_update_immutability_rules_cover_all_boundaries()
    {
        var fixture = new Fixture();
        fixture.Validator.TypeCode.Should().Be(PropertyManagementCodes.Property);

        await AssertConfiguration(() => fixture.ValidateAsync(Fields("Building"), typeCode: "wrong"));
        await AssertPropertyInvalid(() => fixture.ValidateAsync(new Dictionary<string, object?>()));
        await AssertPropertyInvalid(() => fixture.ValidateAsync(Fields(" ")));
        await AssertPropertyInvalid(() => fixture.ValidateAsync(Fields("Garage")));

        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), fixture.CatalogId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(fixture.CatalogId, new Dictionary<string, object?> { ["kind"] = "Building" }));
        await AssertPropertyInvalid(() => fixture.ValidateAsync(Fields("Unit", fixture.ParentId, "1"), isCreate: false));

        fixture.Reader.Reset();
        await fixture.ValidateAsync(BuildingFields(), isCreate: false);
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), fixture.CatalogId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(fixture.CatalogId, new Dictionary<string, object?> { ["kind"] = "unknown" }));
        await fixture.ValidateAsync(BuildingFields(), isCreate: false);
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), fixture.CatalogId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(fixture.CatalogId, new Dictionary<string, object?> { ["kind"] = " " }));
        await fixture.ValidateAsync(BuildingFields(), isCreate: false);
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), fixture.CatalogId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(fixture.CatalogId, new Dictionary<string, object?> { ["kind"] = "building" }));
        await fixture.ValidateAsync(BuildingFields(), isCreate: false);
    }

    [Fact]
    public async Task Building_rejects_parent_unit_number_and_each_missing_address_and_accepts_valid_shape()
    {
        var fixture = new Fixture();
        await AssertPropertyInvalid(() => fixture.ValidateAsync(BuildingFields(parent: fixture.ParentId)));
        await AssertPropertyInvalid(() => fixture.ValidateAsync(BuildingFields(unitNo: "101")));

        foreach (var field in new[] { "address_line1", "city", "state", "zip" })
        {
            var fields = BuildingFields();
            fields[field] = "  ";
            await AssertPropertyInvalid(() => fixture.ValidateAsync(fields));
        }

        await fixture.ValidateAsync(BuildingFields());
        await fixture.ValidateAsync(new Dictionary<string, object?>
        {
            ["kind"] = JsonSerializer.SerializeToElement("building"),
            ["address_line1"] = JsonSerializer.SerializeToElement("1 Main"),
            ["city"] = new Version(1, 2),
            ["state"] = "NY",
            ["zip"] = 10001
        });
    }

    [Fact]
    public async Task Unit_rejects_invalid_identity_number_and_address_shapes()
    {
        var fixture = new Fixture();
        await AssertPropertyInvalid(() => fixture.ValidateAsync(Fields("Unit")));
        await AssertPropertyInvalid(() => fixture.ValidateAsync(Fields("Unit", fixture.CatalogId, "1")));
        await AssertPropertyInvalid(() => fixture.ValidateAsync(Fields("Unit", fixture.ParentId, null)));
        await AssertPropertyInvalid(() => fixture.ValidateAsync(Fields("Unit", fixture.ParentId, " ")));
        await AssertPropertyInvalid(() => fixture.ValidateAsync(Fields("Unit", fixture.ParentId, " 1 ")));

        foreach (var field in new[] { "address_line1", "address_line2", "city", "state", "zip" })
        {
            var fields = Fields("Unit", fixture.ParentId, "1");
            fields[field] = field == "zip" ? JsonSerializer.SerializeToElement(12345) : "not allowed";
            await AssertPropertyInvalid(() => fixture.ValidateAsync(fields));
        }

        var allowed = Fields("Unit", fixture.ParentId, "1");
        allowed["address_line1"] = null;
        allowed["address_line2"] = "  ";
        await fixture.ValidateAsync(allowed);
    }

    [Fact]
    public async Task Unit_parent_must_exist_be_active_correct_catalog_and_building()
    {
        var fixture = new Fixture();
        fixture.Catalogs.Setup(x => x.GetAsync(fixture.ParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null);
        await AssertPropertyInvalid(() => fixture.ValidateValidUnitAsync());

        fixture.SetParentCatalog("another.catalog", isDeleted: false);
        await AssertPropertyInvalid(() => fixture.ValidateValidUnitAsync());

        fixture.SetParentCatalog(PropertyManagementCodes.Property, isDeleted: true);
        await AssertPropertyInvalid(() => fixture.ValidateValidUnitAsync());

        fixture.SetParentCatalog(PropertyManagementCodes.Property, isDeleted: false);
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), fixture.ParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogHeadRow?)null);
        await AssertPropertyInvalid(() => fixture.ValidateValidUnitAsync());

        foreach (object? kind in new object?[] { null, "Unit", "unknown" })
        {
            fixture.SetParentRow(new Dictionary<string, object?> { ["kind"] = kind });
            await AssertPropertyInvalid(() => fixture.ValidateValidUnitAsync());
        }
    }

    [Fact]
    public async Task Unit_cycle_duplicate_and_success_paths_cover_chain_and_query_boundaries()
    {
        var fixture = new Fixture();
        var next = Guid.CreateVersion7();
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), fixture.ParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(fixture.ParentId, new Dictionary<string, object?>
            {
                ["kind"] = "Building",
                ["parent_property_id"] = next
            }));
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), next, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(next, new Dictionary<string, object?> { ["parent_property_id"] = fixture.ParentId }));
        fixture.Reader.Setup(x => x.HasParentChainViolationAsync(
                It.IsAny<CatalogHeadDescriptor>(), fixture.CatalogId, fixture.ParentId,
                "parent_property_id", 32, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await AssertPropertyInvalid(() => fixture.ValidateValidUnitAsync());

        fixture.SetParentRow(new Dictionary<string, object?> { ["kind"] = "Building" });
        fixture.Reader.Setup(x => x.HasParentChainViolationAsync(
                It.IsAny<CatalogHeadDescriptor>(), fixture.CatalogId, fixture.ParentId,
                "parent_property_id", 32, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        fixture.Reader.Setup(x => x.GetPageAsync(
                It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 0, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row(Guid.CreateVersion7(), new Dictionary<string, object?>())]);
        await ((Func<Task>)(() => fixture.ValidateValidUnitAsync())).Should().ThrowAsync<PropertyUnitNoDuplicateException>();

        fixture.Reader.Setup(x => x.GetPageAsync(
                It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 0, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Row(fixture.CatalogId, new Dictionary<string, object?>())]);
        await fixture.ValidateValidUnitAsync();
        await fixture.ValidateValidUnitAsync(parentRepresentation: fixture.ParentId.ToString());
        await fixture.ValidateValidUnitAsync(parentRepresentation: JsonSerializer.SerializeToElement(fixture.ParentId.ToString()));
    }

    [Fact]
    public async Task Parent_chain_is_checked_by_one_bounded_recursive_query()
    {
        var fixture = new Fixture();
        fixture.Reader.Setup(x => x.HasParentChainViolationAsync(
                It.IsAny<CatalogHeadDescriptor>(), fixture.CatalogId, fixture.ParentId,
                "parent_property_id", 32, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        await fixture.ValidateValidUnitAsync();

        fixture.Reader.Setup(x => x.HasParentChainViolationAsync(
                It.IsAny<CatalogHeadDescriptor>(), fixture.CatalogId, fixture.ParentId,
                "parent_property_id", 32, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await AssertPropertyInvalid(() => fixture.ValidateValidUnitAsync());
        fixture.Reader.Verify(x => x.HasParentChainViolationAsync(
            It.IsAny<CatalogHeadDescriptor>(), fixture.CatalogId, fixture.ParentId,
            "parent_property_id", 32, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Metadata_guards_reject_missing_head_and_display_and_cache_valid_head()
    {
        var noHead = new Fixture(Metadata(tables: []));
        await AssertConfiguration(() => noHead.ValidateAsync(BuildingFields(), isCreate: false));

        var noDisplay = new Fixture(Metadata(displayColumn: " "));
        await AssertConfiguration(() => noDisplay.ValidateAsync(BuildingFields(), isCreate: false));

        var valid = new Fixture();
        await valid.ValidateAsync(BuildingFields(), isCreate: false);
        await valid.ValidateAsync(BuildingFields(), isCreate: false);
        valid.CatalogTypes.Verify(x => x.GetRequired(PropertyManagementCodes.Property), Times.Once);
    }

    [Fact]
    public void Scalar_parsers_cover_missing_native_json_and_malformed_representations()
    {
        var normalizeKind = PrivateStatic("NormalizeKind");
        Invoke<string?>(normalizeKind, (object?)null).Should().BeNull();
        Invoke<string?>(normalizeKind, " ").Should().BeNull();
        Invoke<string?>(normalizeKind, "BUILDING").Should().Be("Building");
        Invoke<string?>(normalizeKind, "unit").Should().Be("Unit");
        Invoke<string?>(normalizeKind, "garage").Should().BeNull();

        var readString = PrivateStatic("ReadString");
        Invoke<string?>(readString, new Dictionary<string, object?>(), "value").Should().BeNull();
        Invoke<string?>(readString, new Dictionary<string, object?> { ["value"] = null }, "value").Should().BeNull();
        Invoke<string?>(readString, new Dictionary<string, object?> { ["value"] = "text" }, "value").Should().Be("text");
        Invoke<string?>(readString, new Dictionary<string, object?> { ["value"] = JsonSerializer.SerializeToElement("json") }, "value").Should().Be("json");
        Invoke<string?>(readString, new Dictionary<string, object?> { ["value"] = JsonSerializer.SerializeToElement(42) }, "value").Should().Be("42");
        Invoke<string?>(readString, new Dictionary<string, object?> { ["value"] = new Version(1, 2) }, "value").Should().Be("1.2");

        var id = Guid.CreateVersion7();
        var readGuid = PrivateStatic("ReadGuid");
        Invoke<Guid?>(readGuid, new Dictionary<string, object?>(), "value").Should().BeNull();
        Invoke<Guid?>(readGuid, new Dictionary<string, object?> { ["value"] = null }, "value").Should().BeNull();
        Invoke<Guid?>(readGuid, new Dictionary<string, object?> { ["value"] = id }, "value").Should().Be(id);
        Invoke<Guid?>(readGuid, new Dictionary<string, object?> { ["value"] = id.ToString() }, "value").Should().Be(id);
        Invoke<Guid?>(readGuid, new Dictionary<string, object?> { ["value"] = "invalid" }, "value").Should().BeNull();
        Invoke<Guid?>(readGuid, new Dictionary<string, object?> { ["value"] = JsonSerializer.SerializeToElement(id.ToString()) }, "value").Should().Be(id);
        Invoke<Guid?>(readGuid, new Dictionary<string, object?> { ["value"] = JsonSerializer.SerializeToElement(42) }, "value").Should().BeNull();
        Invoke<Guid?>(readGuid, new Dictionary<string, object?> { ["value"] = 42 }, "value").Should().BeNull();
    }

    private static MethodInfo PrivateStatic(string name) => typeof(PropertyCatalogUpsertValidator)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private static T Invoke<T>(MethodInfo method, params object?[] args) => (T)method.Invoke(null, args)!;

    private static async Task AssertPropertyInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<PropertyValidationException>();

    private static async Task AssertConfiguration(Func<Task> action)
        => await action.Should().ThrowAsync<NgbConfigurationViolationException>();

    private static Dictionary<string, object?> BuildingFields(object? parent = null, object? unitNo = null)
        => new(StringComparer.Ordinal)
        {
            ["kind"] = "Building",
            ["parent_property_id"] = parent,
            ["unit_no"] = unitNo,
            ["address_line1"] = "1 Main",
            ["city"] = "New York",
            ["state"] = "NY",
            ["zip"] = "10001"
        };

    private static Dictionary<string, object?> Fields(object? kind, object? parent = null, object? unitNo = null)
        => new(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["parent_property_id"] = parent,
            ["unit_no"] = unitNo
        };

    private static CatalogHeadRow Row(Guid id, IReadOnlyDictionary<string, object?> fields)
        => new(id, IsMarkedForDeletion: false, Display: null, fields);

    private static CatalogTypeMetadata Metadata(
        IReadOnlyList<CatalogTableMetadata>? tables = null,
        string displayColumn = "name")
        => new(
            PropertyManagementCodes.Property,
            "Property",
            tables ??
            [
                new CatalogTableMetadata(
                    "cat_pm_property",
                    TableKind.Head,
                    [
                        new CatalogColumnMetadata("catalog_id", ColumnType.Guid),
                        new CatalogColumnMetadata("name", ColumnType.String),
                        new CatalogColumnMetadata("kind", ColumnType.String)
                    ],
                    [])
            ],
            new CatalogPresentationMetadata("cat_pm_property", displayColumn),
            new CatalogMetadataVersion(1, "tests"));

    private sealed class Fixture
    {
        public Fixture(CatalogTypeMetadata? metadata = null)
        {
            CatalogTypes.Setup(x => x.GetRequired(PropertyManagementCodes.Property)).Returns(metadata ?? Metadata());
            SetParentCatalog(PropertyManagementCodes.Property, isDeleted: false);
            SetParentRow(new Dictionary<string, object?> { ["kind"] = "Building" });
            Reader.Setup(x => x.GetPageAsync(
                    It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 0, 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Validator = new PropertyCatalogUpsertValidator(CatalogTypes.Object, Catalogs.Object, Reader.Object);
        }

        public Guid CatalogId { get; } = Guid.CreateVersion7();
        public Guid ParentId { get; set; } = Guid.CreateVersion7();
        public Mock<ICatalogTypeRegistry> CatalogTypes { get; } = new();
        public Mock<ICatalogRepository> Catalogs { get; } = new();
        public Mock<ICatalogReader> Reader { get; } = new();
        public PropertyCatalogUpsertValidator Validator { get; }

        public void SetParentCatalog(string code, bool isDeleted)
            => Catalogs.Setup(x => x.GetAsync(ParentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CatalogRecord
                {
                    Id = ParentId,
                    CatalogCode = code,
                    IsDeleted = isDeleted,
                    CreatedAtUtc = DateTime.UnixEpoch,
                    UpdatedAtUtc = DateTime.UnixEpoch
                });

        public void SetParentRow(IReadOnlyDictionary<string, object?> fields)
            => Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), ParentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Row(ParentId, fields));

        public Task ValidateValidUnitAsync(object? parentRepresentation = null)
            => ValidateAsync(Fields("Unit", parentRepresentation ?? ParentId, "101"));

        public Task ValidateAsync(
            IReadOnlyDictionary<string, object?> fields,
            bool isCreate = true,
            string typeCode = PropertyManagementCodes.Property)
            => Validator.ValidateUpsertAsync(
                new CatalogUpsertValidationContext(typeCode, CatalogId, isCreate, fields),
                default);
    }
}
