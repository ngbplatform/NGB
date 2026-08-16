using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Core.Catalogs;
using NGB.Core.Dimensions;
using NGB.Core.Reporting;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Universal;
using NGB.PropertyManagement.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Reporting;

public sealed class PropertyManagementPropertyDimensionScopeExpanderFullCoverageTests
{
    private static readonly Guid PropertyDimensionId = (Guid)typeof(PropertyManagementPropertyDimensionScopeExpander)
        .GetField("PropertyDimensionId", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
        .GetValue(null)!;

    [Fact]
    public async Task Invalid_arguments_are_rejected_and_irrelevant_scopes_are_preserved_by_identity()
    {
        var fixture = new Fixture();
        var irrelevant = new DimensionScopeBag(
        [
            new DimensionScope(Guid.CreateVersion7(), [Guid.CreateVersion7()], includeDescendants: true),
            new DimensionScope(PropertyDimensionId, [Guid.CreateVersion7()], includeDescendants: false)
        ]);

        await ((Func<Task>)(() => fixture.Sut.ExpandAsync(" ", irrelevant, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.ExpandAsync(AccountingReportCodes.TrialBalance, null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        (await fixture.Sut.ExpandAsync(AccountingReportCodes.TrialBalance, DimensionScopeBag.Empty, default))
            .Should().BeSameAs(DimensionScopeBag.Empty);
        (await fixture.Sut.ExpandAsync("unsupported", irrelevant, default)).Should().BeSameAs(irrelevant);
        (await fixture.Sut.ExpandAsync(AccountingReportCodes.TrialBalance, irrelevant, default)).Should().BeSameAs(irrelevant);
        fixture.Reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Building_scope_expands_active_nested_hierarchy_and_snapshot_is_cached()
    {
        var fixture = new Fixture();
        var root = Guid.CreateVersion7();
        var unit = Guid.CreateVersion7();
        var nestedBuilding = Guid.CreateVersion7();
        var nestedUnit = Guid.CreateVersion7();
        var unrelatedDimension = Guid.CreateVersion7();
        var unrelatedValue = Guid.CreateVersion7();
        fixture.Reader.Setup(x => x.GetPageAsync(
                It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 0, 512,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                Row(root, new Dictionary<string, object?> { ["kind"] = "building" }),
                Row(unit, new Dictionary<string, object?> { ["kind"] = "Unit", ["parent_property_id"] = root }),
                Row(nestedBuilding, new Dictionary<string, object?>
                {
                    ["kind"] = "Building",
                    ["parent_property_id"] = root.ToString()
                }),
                Row(nestedUnit, new Dictionary<string, object?>
                {
                    ["kind"] = "Unit",
                    ["parent_property_id"] = JsonSerializer.SerializeToElement(nestedBuilding.ToString())
                }),
                Row(Guid.CreateVersion7(), new Dictionary<string, object?> { ["kind"] = 42, ["parent_property_id"] = null }),
                Row(Guid.CreateVersion7(), new Dictionary<string, object?> { ["kind"] = "Unit", ["parent_property_id"] = Guid.Empty })
            ]);
        var scopes = new DimensionScopeBag(
        [
            new DimensionScope(PropertyDimensionId, [root, unit], includeDescendants: true),
            new DimensionScope(unrelatedDimension, [unrelatedValue], includeDescendants: true)
        ]);

        var first = await fixture.Sut.ExpandAsync(AccountingReportCodes.BalanceSheet, scopes, default);
        var second = await fixture.Sut.ExpandAsync(AccountingReportCodes.IncomeStatement, scopes, default);

        first.Should().NotBeSameAs(scopes);
        first.Single(x => x.DimensionId == PropertyDimensionId).ValueIds.Should()
            .BeEquivalentTo([root, unit, nestedBuilding, nestedUnit]);
        first.Single(x => x.DimensionId == PropertyDimensionId).IncludeDescendants.Should().BeFalse();
        first.Single(x => x.DimensionId == unrelatedDimension).ValueIds.Should().Equal(unrelatedValue);
        second.Single(x => x.DimensionId == PropertyDimensionId).ValueIds.Should()
            .BeEquivalentTo([root, unit, nestedBuilding, nestedUnit]);
        fixture.Reader.Verify(x => x.GetPageAsync(
            It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 0, 512,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Full_page_loads_next_page_until_empty()
    {
        var fixture = new Fixture();
        var root = Guid.CreateVersion7();
        var rows = Enumerable.Range(0, 512)
            .Select(index => Row(index == 0 ? root : Guid.CreateVersion7(),
                new Dictionary<string, object?> { ["kind"] = index == 0 ? "Building" : "Unit" }))
            .ToArray();
        fixture.Reader.Setup(x => x.GetPageAsync(
                It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 0, 512,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        fixture.Reader.Setup(x => x.GetPageAsync(
                It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 512, 512,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await fixture.Sut.ExpandAsync(
            AccountingReportCodes.GeneralJournal,
            Bag(root),
            default);

        result[0].ValueIds.Should().Equal(root);
        fixture.Reader.Verify(x => x.GetPageAsync(
            It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 512, 512,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Property_absent_from_active_snapshot_is_loaded_from_storage()
    {
        var fixture = new Fixture();
        var propertyId = Guid.CreateVersion7();
        fixture.EmptyHierarchy();
        fixture.Catalogs.Setup(x => x.GetAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(propertyId));
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), propertyId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(propertyId, new Dictionary<string, object?> { ["kind"] = "Unit" }));

        var result = await fixture.Sut.ExpandAsync(AccountingReportCodes.AccountCard, Bag(propertyId), default);

        result[0].ValueIds.Should().Equal(propertyId);
        fixture.Types.Verify(x => x.GetRequired(PropertyManagementCodes.Property), Times.Once);
    }

    [Fact]
    public async Task Missing_wrong_type_and_deleted_property_are_rejected()
    {
        var missing = new Fixture();
        var missingId = Guid.CreateVersion7();
        missing.EmptyHierarchy();
        missing.Catalogs.Setup(x => x.GetAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null);
        await AssertInvalidAsync(missing.Sut, missingId);

        var wrong = new Fixture();
        var wrongId = Guid.CreateVersion7();
        wrong.EmptyHierarchy();
        wrong.Catalogs.Setup(x => x.GetAsync(wrongId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(wrongId, code: "other"));
        await AssertInvalidAsync(wrong.Sut, wrongId);

        var deleted = new Fixture();
        var deletedId = Guid.CreateVersion7();
        deleted.EmptyHierarchy();
        deleted.Catalogs.Setup(x => x.GetAsync(deletedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(deletedId, isDeleted: true));
        await AssertInvalidAsync(deleted.Sut, deletedId);
    }

    [Fact]
    public async Task Incomplete_fallback_property_data_is_a_configuration_error()
    {
        var fixture = new Fixture();
        var propertyId = Guid.CreateVersion7();
        fixture.EmptyHierarchy();
        fixture.Catalogs.Setup(x => x.GetAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(propertyId));
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), propertyId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogHeadRow?)null);

        var act = () => fixture.Sut.ExpandAsync(AccountingReportCodes.GeneralLedgerAggregated, Bag(propertyId), default);

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task Missing_head_or_display_metadata_is_rejected()
    {
        var noHead = new Fixture(Metadata(tables: []));
        var noHeadAct = () => noHead.Sut.ExpandAsync(AccountingReportCodes.TrialBalance,
            Bag(Guid.CreateVersion7()), default);
        await noHeadAct.Should().ThrowAsync<NgbConfigurationViolationException>();

        var noDisplay = new Fixture(Metadata(displayColumn: " "));
        var noDisplayAct = () => noDisplay.Sut.ExpandAsync(AccountingReportCodes.TrialBalance,
            Bag(Guid.CreateVersion7()), default);
        await noDisplayAct.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    private static async Task AssertInvalidAsync(PropertyManagementPropertyDimensionScopeExpander sut, Guid propertyId)
    {
        var act = () => sut.ExpandAsync(AccountingReportCodes.TrialBalance, Bag(propertyId), default);
        await act.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    private static DimensionScopeBag Bag(Guid propertyId)
        => new([new DimensionScope(PropertyDimensionId, [propertyId], includeDescendants: true)]);

    private static CatalogHeadRow Row(Guid id, IReadOnlyDictionary<string, object?> fields)
        => new(id, IsMarkedForDeletion: false, Display: id.ToString(), fields);

    private static CatalogRecord Catalog(Guid id, string code = PropertyManagementCodes.Property, bool isDeleted = false)
        => new()
        {
            Id = id,
            CatalogCode = code,
            IsDeleted = isDeleted,
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch
        };

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
            Types.Setup(x => x.GetRequired(PropertyManagementCodes.Property)).Returns(metadata ?? Metadata());
            Sut = new PropertyManagementPropertyDimensionScopeExpander(Types.Object, Catalogs.Object, Reader.Object);
        }

        public Mock<ICatalogTypeRegistry> Types { get; } = new(MockBehavior.Strict);
        public Mock<ICatalogRepository> Catalogs { get; } = new(MockBehavior.Strict);
        public Mock<ICatalogReader> Reader { get; } = new(MockBehavior.Strict);
        public PropertyManagementPropertyDimensionScopeExpander Sut { get; }

        public void EmptyHierarchy()
            => Reader.Setup(x => x.GetPageAsync(
                    It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 0, 512,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
    }
}
