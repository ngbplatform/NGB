using System.Reflection;
using FluentAssertions;
using Moq;
using NGB.Core.AuditLog;
using NGB.Core.Catalogs;
using NGB.Definitions.Catalogs.Validation;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Catalogs;
using NGB.PropertyManagement.Runtime.Catalogs;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs.Validation;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Catalogs;

public sealed class PropertyBulkCreateUnitsServiceFullCoverageTests
{
    [Fact]
    public async Task Public_operations_reject_every_invalid_request_boundary()
    {
        var service = new Harness().Service;

        await AssertThrows<NgbArgumentRequiredException>(() => service.BulkCreateUnitsAsync(null!, default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() => service.BulkCreateUnitsAsync(Request(buildingId: Guid.Empty), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() => service.BulkCreateUnitsAsync(Request(step: 0), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() => service.BulkCreateUnitsAsync(Request(from: 0), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() => service.BulkCreateUnitsAsync(Request(to: 0), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() => service.BulkCreateUnitsAsync(Request(from: 2, to: 1), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() => service.BulkCreateUnitsAsync(Request(format: " "), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() => service.BulkCreateUnitsAsync(Request(format: "Unit"), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() => service.BulkCreateUnitsAsync(Request(floorSize: 0), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() => service.BulkCreateUnitsAsync(Request(floorSize: -1), default));
    }

    [Fact]
    public async Task Metadata_must_have_a_head_table_and_nonblank_display_column()
    {
        var noHead = new Harness { Metadata = Metadata(tables: []) };
        await AssertThrows<NgbConfigurationViolationException>(() => noHead.Service.DryRunAsync(Request(), default));

        var blankDisplay = new Harness { Metadata = Metadata(displayColumn: " ") };
        await AssertThrows<NgbConfigurationViolationException>(() => blankDisplay.Service.DryRunAsync(Request(), default));
    }

    [Fact]
    public async Task Generation_rejects_invalid_format_empty_output_and_oversized_request()
    {
        var harness = new Harness();

        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() =>
            harness.Service.DryRunAsync(Request(format: "{0}-{2}"), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() =>
            harness.Service.DryRunAsync(Request(format: "{0:;;}"), default));
        await AssertThrows<PropertyBulkCreateUnitsValidationException>(() =>
            harness.Service.DryRunAsync(Request(from: 1, to: 5001), default));
    }

    [Fact]
    public async Task Dry_run_loads_all_pages_filters_bad_rows_bounds_samples_and_deduplicates_generated_numbers()
    {
        var rows = Enumerable.Range(0, 2000)
            .Select(index => index switch
            {
                0 => Row(new Dictionary<string, object?>()),
                1 => Row(new Dictionary<string, object?> { ["unit_no"] = null }),
                2 => Row(new Dictionary<string, object?> { ["unit_no"] = " " }),
                3 => Row(new Dictionary<string, object?> { ["unit_no"] = " 001 " }),
                _ => Row(new Dictionary<string, object?> { ["unit_no"] = $"existing-{index}" })
            })
            .ToArray();
        var harness = new Harness { ExistingRows = rows };

        var response = await harness.Service.DryRunAsync(Request(from: 1, to: 120), default);

        response.IsDryRun.Should().BeTrue();
        response.RequestedCount.Should().Be(120);
        response.DuplicateCount.Should().Be(1);
        response.CreatedCount.Should().Be(0);
        response.WouldCreateCount.Should().Be(119);
        response.PreviewUnitNosSample.Should().HaveCount(50);
        response.CreatedUnitNosSample.Should().BeEmpty();
        harness.ReaderCalls.Should().Equal(0, 2000);
        harness.CreatedCatalogs.Should().BeEmpty();

        var duplicateFormat = await new Harness().Service.DryRunAsync(
            Request(from: 1, to: 3, format: "Unit-{0:;;}"),
            default);
        duplicateFormat.RequestedCount.Should().Be(1);
        duplicateFormat.PreviewUnitNosSample.Should().Equal("Unit-");
    }

    [Fact]
    public async Task Real_run_writes_catalog_heads_and_audit_with_floor_format_and_handles_no_work_paths()
    {
        var harness = new Harness { EnableAudit = true };
        var response = await harness.Service.BulkCreateUnitsAsync(
            Request(from: 1, to: 3, format: "{1}-{0:000}", floorSize: 2),
            default);

        response.IsDryRun.Should().BeFalse();
        response.CreatedCount.Should().Be(3);
        response.WouldCreateCount.Should().Be(3);
        response.CreatedUnitNosSample.Should().Equal("1-001", "1-002", "2-003");
        harness.CreatedCatalogs.Should().HaveCount(3);
        harness.WrittenHeads.Should().ContainSingle().Which.Should().HaveCount(3);
        harness.AuditBatches.Should().ContainSingle().Which.Should().HaveCount(3);
        harness.CommitCount.Should().Be(1);

        var withoutAudit = new Harness();
        (await withoutAudit.Service.BulkCreateUnitsAsync(Request(), default)).CreatedCount.Should().Be(1);
        withoutAudit.AuditBatches.Should().BeEmpty();

        var allDuplicates = new Harness
        {
            ExistingRows = [Row(new Dictionary<string, object?> { ["unit_no"] = "001" })]
        };
        var duplicateResponse = await allDuplicates.Service.BulkCreateUnitsAsync(Request(), default);
        duplicateResponse.CreatedCount.Should().Be(0);
        duplicateResponse.DuplicateCount.Should().Be(1);
        allDuplicates.CreatedCatalogs.Should().BeEmpty();
        allDuplicates.WrittenHeads.Should().BeEmpty();
    }

    [Fact]
    public void Audit_change_serialization_covers_null_and_nonnull_old_and_new_values()
    {
        var method = typeof(PropertyBulkCreateUnitsService).GetMethod(
            "CreateAuditChange",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var bothNull = (AuditFieldChange)method.Invoke(null, ["field", null, null])!;
        bothNull.OldValueJson.Should().BeNull();
        bothNull.NewValueJson.Should().BeNull();

        var populated = (AuditFieldChange)method.Invoke(null, ["field", "before", 42])!;
        populated.OldValueJson.Should().Be("\"before\"");
        populated.NewValueJson.Should().Be("42");
    }

    private sealed class Harness
    {
        private readonly Mock<IUnitOfWork> _uow = new(MockBehavior.Strict);
        private readonly Mock<IAdvisoryLockManager> _locks = new(MockBehavior.Strict);
        private readonly Mock<ICatalogTypeRegistry> _types = new(MockBehavior.Strict);
        private readonly Mock<ICatalogRepository> _catalogs = new(MockBehavior.Strict);
        private readonly Mock<ICatalogReader> _reader = new(MockBehavior.Strict);
        private readonly Mock<ICatalogWriter> _writer = new(MockBehavior.Strict);
        private readonly Mock<ICatalogValidatorResolver> _validators = new(MockBehavior.Strict);
        private readonly Mock<IAuditLogService> _audit = new(MockBehavior.Strict);

        public CatalogTypeMetadata Metadata { get; init; } = PropertyBulkCreateUnitsServiceFullCoverageTests.Metadata();
        public IReadOnlyList<CatalogHeadRow> ExistingRows { get; init; } = [];
        public bool EnableAudit { get; init; }
        public List<int> ReaderCalls { get; } = [];
        public List<CatalogRecord> CreatedCatalogs { get; } = [];
        public List<IReadOnlyList<CatalogHeadWriteRow>> WrittenHeads { get; } = [];
        public List<IReadOnlyList<AuditLogWriteRequest>> AuditBatches { get; } = [];
        public int CommitCount { get; private set; }

        public PropertyBulkCreateUnitsService Service
        {
            get
            {
                Configure();
                return new PropertyBulkCreateUnitsService(
                    _uow.Object,
                    _locks.Object,
                    _types.Object,
                    _catalogs.Object,
                    _reader.Object,
                    _writer.Object,
                    _validators.Object,
                    TimeProvider.System,
                    EnableAudit ? _audit.Object : null);
            }
        }

        private void Configure()
        {
            _uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
            _uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Callback(() => CommitCount++)
                .Returns(Task.CompletedTask);
            _uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _locks.Setup(x => x.LockCatalogAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _types.Setup(x => x.GetRequired(PropertyManagementCodes.Property)).Returns(Metadata);

            var validator = new Mock<ICatalogUpsertValidator>(MockBehavior.Strict);
            validator.Setup(x => x.ValidateUpsertAsync(
                    It.Is<CatalogUpsertValidationContext>(context =>
                        context.TypeCode == PropertyManagementCodes.Property
                        && context.IsCreate
                        && Equals(context.Fields["unit_no"], "__probe__")),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _validators.Setup(x => x.ResolveUpsertValidators(PropertyManagementCodes.Property))
                .Returns([validator.Object]);
            _reader.Setup(x => x.GetPageAsync(
                    It.IsAny<CatalogHeadDescriptor>(),
                    It.IsAny<CatalogQuery>(),
                    It.IsAny<int>(),
                    2000,
                    It.IsAny<CancellationToken>()))
                .Callback<CatalogHeadDescriptor, CatalogQuery, int, int, CancellationToken>(
                    (_, _, offset, _, _) => ReaderCalls.Add(offset))
                .ReturnsAsync((CatalogHeadDescriptor _, CatalogQuery _, int offset, int _, CancellationToken _) =>
                    offset == 0 ? ExistingRows : []);
            _catalogs.Setup(x => x.CreateManyAsync(
                    It.IsAny<IReadOnlyList<CatalogRecord>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<CatalogRecord>, CancellationToken>((records, _) => CreatedCatalogs.AddRange(records))
                .Returns(Task.CompletedTask);
            _writer.Setup(x => x.UpsertHeadsAsync(
                    It.IsAny<CatalogHeadDescriptor>(),
                    It.IsAny<IReadOnlyList<CatalogHeadWriteRow>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<CatalogHeadDescriptor, IReadOnlyList<CatalogHeadWriteRow>, CancellationToken>(
                    (_, rows, _) => WrittenHeads.Add(rows))
                .Returns(Task.CompletedTask);
            _audit.Setup(x => x.WriteBatchAsync(
                    It.IsAny<IReadOnlyList<AuditLogWriteRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<AuditLogWriteRequest>, CancellationToken>(
                    (requests, _) => AuditBatches.Add(requests))
                .Returns(Task.CompletedTask);
        }
    }

    private static PropertyBulkCreateUnitsRequest Request(
        Guid? buildingId = null,
        int from = 1,
        int to = 1,
        int step = 1,
        string format = "{0:000}",
        int? floorSize = null)
        => new()
        {
            BuildingId = buildingId ?? Guid.CreateVersion7(),
            FromInclusive = from,
            ToInclusive = to,
            Step = step,
            UnitNoFormat = format,
            FloorSize = floorSize
        };

    private static CatalogTypeMetadata Metadata(
        IReadOnlyList<CatalogTableMetadata>? tables = null,
        string displayColumn = "unit_no")
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
                        new CatalogColumnMetadata("kind", ColumnType.String),
                        new CatalogColumnMetadata("parent_property_id", ColumnType.Guid),
                        new CatalogColumnMetadata("unit_no", ColumnType.String)
                    ],
                    [])
            ],
            new CatalogPresentationMetadata("cat_pm_property", displayColumn),
            new CatalogMetadataVersion(1, "tests"));

    private static CatalogHeadRow Row(IReadOnlyDictionary<string, object?> fields)
        => new(Guid.CreateVersion7(), false, null, fields);

    private static Task AssertThrows<T>(Func<Task> action) where T : Exception
        => action.Should().ThrowAsync<T>();
}
