using System.Data;
using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.ReferenceRegisters;

public sealed class PostgresReferenceRegisterRecordsReaderFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DimensionSetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RecorderId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime AsOf = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Slice_last_validates_arguments_modes_metadata_and_absent_data()
    {
        var independent = Fixture(ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent);
        Func<Task> emptyRegister = () => independent.Reader.SliceLastAsync(Guid.Empty, DimensionSetId, AsOf, null, default);
        Func<Task> localAsOf = () => independent.Reader.SliceLastAsync(
            RegisterId, DimensionSetId, DateTime.SpecifyKind(AsOf, DateTimeKind.Local), null, default);
        await emptyRegister.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await localAsOf.Should().ThrowAsync<NgbArgumentInvalidException>();

        var missingRegister = Fixture(ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent);
        missingRegister.Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        Func<Task> notFound = () => missingRegister.Reader.SliceLastAsync(RegisterId, DimensionSetId, AsOf, null, default);
        await notFound.Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        Func<Task> forbiddenRecorder = () => independent.Reader.SliceLastAsync(
            RegisterId, DimensionSetId, AsOf, RecorderId, default);
        await forbiddenRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        var subordinate = Fixture(ReferenceRegisterPeriodicity.Day, ReferenceRegisterRecordMode.SubordinateToRecorder);
        Func<Task> nullRecorder = () => subordinate.Reader.SliceLastAsync(
            RegisterId, DimensionSetId, AsOf, null, default);
        Func<Task> emptyRecorder = () => subordinate.Reader.SliceLastAsync(
            RegisterId, DimensionSetId, AsOf, Guid.Empty, default);
        await nullRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        await emptyRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        var noTable = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent, tableExists: false);
        (await noTable.Reader.SliceLastAsync(RegisterId, Guid.Empty, AsOf, null, default)).Should().BeNull();

        var unsafeField = Field("Unsafe", "unsafe");
        var unsafeMetadata = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            fields: [unsafeField]);
        Func<Task> unsafeColumn = () => unsafeMetadata.Reader.SliceLastAsync(
            RegisterId, DimensionSetId, AsOf, null, default);
        await unsafeColumn.Should().ThrowAsync<NgbConfigurationViolationException>();

        var noRows = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            rows: RecordRows());
        (await noRows.Reader.SliceLastAsync(RegisterId, DimensionSetId, AsOf, null, default)).Should().BeNull();
    }

    [Fact]
    public async Task Slice_last_and_effective_moment_map_periodic_subordinate_and_non_periodic_records()
    {
        var fields = new[] { Field("value_text", "value") };
        var rows = RecordRows((1L, DimensionSetId, AsOf, AsOf.Date, RecorderId, AsOf, false, "shown"));
        var subordinate = Fixture(
            ReferenceRegisterPeriodicity.Day,
            ReferenceRegisterRecordMode.SubordinateToRecorder,
            fields: fields,
            rows: rows);

        var current = await subordinate.Reader.SliceLastAsync(
            RegisterId, DimensionSetId, AsOf, RecorderId, default);
        var effective = await subordinate.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, DimensionSetId, AsOf, AsOf.AddHours(1), RecorderId, default);

        current.Should().NotBeNull();
        current!.Values.Should().Contain("value", "shown");
        effective.Should().BeEquivalentTo(current);
        subordinate.Connection.Commands.Should().Contain(x =>
            x.CommandText.Contains("@EffectiveAsOfUtc", StringComparison.Ordinal)
            && x.CommandText.Contains("@BucketEffectiveUtc", StringComparison.Ordinal));

        var nonPeriodic = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            rows: RecordRows((2L, Guid.Empty, null, null, null, AsOf, true, null)));
        var delegated = await nonPeriodic.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, Guid.Empty, AsOf, AsOf, null, default);
        delegated.Should().NotBeNull();
        delegated!.PeriodUtc.Should().BeNull();
        delegated.RecorderDocumentId.Should().BeNull();

        Func<Task> localEffective = () => nonPeriodic.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, DimensionSetId, DateTime.SpecifyKind(AsOf, DateTimeKind.Local), AsOf, null, default);
        Func<Task> localRecorded = () => nonPeriodic.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, DimensionSetId, AsOf, DateTime.SpecifyKind(AsOf, DateTimeKind.Local), null, default);
        await localEffective.Should().ThrowAsync<NgbArgumentInvalidException>();
        await localRecorded.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task Effective_moment_rejects_invalid_metadata_and_recorder_modes_and_handles_absent_storage()
    {
        var missingRegister = Fixture(
            ReferenceRegisterPeriodicity.Day,
            ReferenceRegisterRecordMode.Independent);
        missingRegister.Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        Func<Task> notFound = () => missingRegister.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, null, default);
        await notFound.Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        var subordinate = Fixture(
            ReferenceRegisterPeriodicity.Day,
            ReferenceRegisterRecordMode.SubordinateToRecorder);
        Func<Task> nullRecorder = () => subordinate.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, null, default);
        Func<Task> emptyRecorder = () => subordinate.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, Guid.Empty, default);
        await nullRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        await emptyRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        var independent = Fixture(
            ReferenceRegisterPeriodicity.Day,
            ReferenceRegisterRecordMode.Independent);
        Func<Task> forbiddenRecorder = () => independent.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, RecorderId, default);
        await forbiddenRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        var noTable = Fixture(
            ReferenceRegisterPeriodicity.Day,
            ReferenceRegisterRecordMode.Independent,
            tableExists: false);
        (await noTable.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, null, default)).Should().BeNull();

        var noRows = Fixture(
            ReferenceRegisterPeriodicity.Day,
            ReferenceRegisterRecordMode.Independent,
            rows: RecordRows());
        (await noRows.Reader.SliceLastForEffectiveMomentAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, null, default)).Should().BeNull();
    }

    [Fact]
    public async Task Slice_last_all_covers_paging_modes_table_absence_and_mapping()
    {
        var independent = Fixture(
            ReferenceRegisterPeriodicity.Month,
            ReferenceRegisterRecordMode.Independent,
            fields: [Field("value_text", "value")],
            rows: RecordRows(
                (1L, DimensionSetId, AsOf, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), null, AsOf, false, DBNull.Value),
                (2L, Guid.NewGuid(), null, null, null, AsOf.AddMinutes(-1), true, "second")));
        Func<Task> invalidLimit = async () => await independent.Reader.SliceLastAllAsync(
            RegisterId, AsOf, null, null, 0, default);
        await invalidLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var first = await independent.Reader.SliceLastAllAsync(RegisterId, AsOf, null, null, 10, default);
        var after = await independent.Reader.SliceLastAllAsync(RegisterId, AsOf, null, DimensionSetId, 10, default);
        var visibleOnly = await independent.Reader.SliceLastAllPageAsync(
            RegisterId, AsOf, null, null, 10, includeDeleted: false, default);
        var rawVisibleScan = await independent.Reader.ScanSliceLastAllForVisiblePageAsync(
            RegisterId, AsOf, null, null, pageSize: 10, maxScanPages: 25, default);
        first.Should().HaveCount(2);
        after.Should().HaveCount(2);
        visibleOnly.Should().HaveCount(2);
        rawVisibleScan.Should().HaveCount(2);
        first[0].Values.Should().Contain("value", null);
        independent.Connection.Commands.Should().Contain(x =>
            x.CommandText.Contains("dimension_set_id > @AfterDimensionSetId", StringComparison.Ordinal));
        independent.Connection.Commands.Should().Contain(x =>
            x.CommandText.Contains("WHERE @IncludeDeleted OR \"IsDeleted\" = FALSE", StringComparison.Ordinal));
        independent.Connection.Commands.Should().Contain(x =>
            x.CommandText.Contains("numbered_rows", StringComparison.Ordinal)
            && x.CommandText.Contains("__VisibleCount", StringComparison.Ordinal));

        Func<Task> invalidScanPageSize = async () => await independent.Reader.ScanSliceLastAllForVisiblePageAsync(
            RegisterId, AsOf, null, null, pageSize: 0, maxScanPages: 25, default);
        Func<Task> invalidScanPages = async () => await independent.Reader.ScanSliceLastAllForVisiblePageAsync(
            RegisterId, AsOf, null, null, pageSize: 10, maxScanPages: 0, default);
        await invalidScanPageSize.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidScanPages.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        (await independent.Reader.ScanSliceLastAllForVisiblePageAsync(
            RegisterId, AsOf, null, null, int.MaxValue, 2, default)).Should().HaveCount(2);

        var subordinate = Fixture(
            ReferenceRegisterPeriodicity.Day,
            ReferenceRegisterRecordMode.SubordinateToRecorder,
            rows: RecordRows());
        Func<Task> required = async () => await subordinate.Reader.SliceLastAllAsync(
            RegisterId, AsOf, null, null, 10, default);
        await required.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        (await subordinate.Reader.SliceLastAllAsync(RegisterId, AsOf, RecorderId, null, 10, default))
            .Should().BeEmpty();

        var noTable = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            tableExists: false);
        (await noTable.Reader.SliceLastAllAsync(RegisterId, AsOf, null, null, 10, default)).Should().BeEmpty();

        Func<Task> forbidden = async () => await independent.Reader.SliceLastAllAsync(
            RegisterId, AsOf, RecorderId, null, 10, default);
        await forbidden.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        var missingRegister = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent);
        missingRegister.Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        Func<Task> notFound = async () => await missingRegister.Reader.SliceLastAllAsync(
            RegisterId, AsOf, null, null, 10, default);
        await notFound.Should().ThrowAsync<ReferenceRegisterNotFoundException>();
    }

    [Fact]
    public async Task Dimension_filtered_slice_validates_dimensions_and_builds_multi_pair_filter()
    {
        var sut = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            fields: [Field("value_text", "value")],
            rows: RecordRows((1L, DimensionSetId, null, null, null, AsOf, false, null)));
        var d1 = new DimensionValue(Guid.NewGuid(), Guid.NewGuid());
        var d2 = new DimensionValue(Guid.NewGuid(), Guid.NewGuid());
        Func<Task> nullDimensions = async () => await sut.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, null!, null, null, 10, default);
        Func<Task> emptyDimensions = async () => await sut.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [], null, null, 10, default);
        Func<Task> invalidLimit = async () => await sut.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1], null, null, 0, default);
        Func<Task> duplicate = async () => await sut.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1, new DimensionValue(d1.DimensionId, Guid.NewGuid())], null, null, 10, default);
        await nullDimensions.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyDimensions.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await duplicate.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        var rows = await sut.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1, d2], null, DimensionSetId, 10, default);
        var visibleRows = await sut.Reader.SliceLastAllFilteredPageByDimensionsAsync(
            RegisterId, AsOf, [d1, d2], null, DimensionSetId, 10, includeDeleted: false, default);
        var rawVisibleScan = await sut.Reader.ScanSliceLastAllFilteredForVisiblePageAsync(
            RegisterId, AsOf, [d1, d2], null, DimensionSetId, 10, 25, default);
        rows.Should().ContainSingle();
        visibleRows.Should().ContainSingle();
        rawVisibleScan.Should().ContainSingle();
        sut.Connection.Commands.Last().CommandText.Should()
            .Contain("s.dimension_id = @D0").And.Contain("s.dimension_id = @D1")
            .And.Contain("HAVING COUNT(*) = @DimCount")
            .And.Contain("numbered_rows");

        var missingRegister = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent);
        missingRegister.Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        Func<Task> notFound = async () => await missingRegister.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1], null, null, 10, default);
        await notFound.Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        var subordinate = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.SubordinateToRecorder);
        Func<Task> nullRecorder = async () => await subordinate.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1], null, null, 10, default);
        Func<Task> emptyRecorder = async () => await subordinate.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1], Guid.Empty, null, 10, default);
        await nullRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        await emptyRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        (await subordinate.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1], RecorderId, null, 10, default)).Should().BeEmpty();

        Func<Task> forbiddenRecorder = async () => await sut.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1], RecorderId, null, 10, default);
        await forbiddenRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        var noTable = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            tableExists: false);
        (await noTable.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1], null, null, 10, default)).Should().BeEmpty();

        var unsafeMetadata = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            fields: [Field("Unsafe", "unsafe")]);
        Func<Task> unsafeColumn = async () => await unsafeMetadata.Reader.SliceLastAllFilteredByDimensionsAsync(
            RegisterId, AsOf, [d1], null, null, 10, default);
        await unsafeColumn.Should().ThrowAsync<NgbConfigurationViolationException>();

    }

    [Fact]
    public async Task Recorder_listing_validates_cursor_and_returns_only_for_subordinate_registers()
    {
        var subordinate = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.SubordinateToRecorder,
            fields: [Field("value_text", "value")],
            rows: RecordRows((1L, DimensionSetId, null, null, RecorderId, AsOf, false, null)));
        Func<Task> emptyRegister = async () => await subordinate.Reader.ListByRecorderDocumentAsync(
            Guid.Empty, RecorderId, null, null, 10, default);
        Func<Task> emptyRecorder = async () => await subordinate.Reader.ListByRecorderDocumentAsync(
            RegisterId, Guid.Empty, null, null, 10, default);
        Func<Task> localCursor = async () => await subordinate.Reader.ListByRecorderDocumentAsync(
            RegisterId, RecorderId, DateTime.SpecifyKind(AsOf, DateTimeKind.Local), 1, 10, default);
        Func<Task> partialTime = async () => await subordinate.Reader.ListByRecorderDocumentAsync(
            RegisterId, RecorderId, AsOf, null, 10, default);
        Func<Task> partialId = async () => await subordinate.Reader.ListByRecorderDocumentAsync(
            RegisterId, RecorderId, null, 1, 10, default);
        Func<Task> invalidLimit = async () => await subordinate.Reader.ListByRecorderDocumentAsync(
            RegisterId, RecorderId, null, null, 0, default);
        await emptyRegister.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await emptyRecorder.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await localCursor.Should().ThrowAsync<NgbArgumentInvalidException>();
        await partialTime.Should().ThrowAsync<NgbArgumentInvalidException>();
        await partialId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await invalidLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        (await subordinate.Reader.ListByRecorderDocumentAsync(RegisterId, RecorderId, null, null, 10, default))
            .Should().ContainSingle();
        (await subordinate.Reader.ListByRecorderDocumentAsync(RegisterId, RecorderId, AsOf, 2, 10, default))
            .Should().ContainSingle();
        subordinate.Connection.Commands.Should().Contain(x =>
            x.CommandText.Contains("@BeforeRecordedAtUtc, @BeforeRecordId", StringComparison.Ordinal));

        var emptyFields = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.SubordinateToRecorder,
            rows: RecordRows());
        (await emptyFields.Reader.ListByRecorderDocumentAsync(
            RegisterId, RecorderId, null, null, 10, default)).Should().BeEmpty();

        var independent = Fixture(ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent);
        (await independent.Reader.ListByRecorderDocumentAsync(RegisterId, RecorderId, null, null, 10, default))
            .Should().BeEmpty();

        var missingRegister = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.SubordinateToRecorder);
        missingRegister.Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        Func<Task> notFound = async () => await missingRegister.Reader.ListByRecorderDocumentAsync(
            RegisterId, RecorderId, null, null, 10, default);
        await notFound.Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        var noTable = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.SubordinateToRecorder,
            tableExists: false);
        (await noTable.Reader.ListByRecorderDocumentAsync(RegisterId, RecorderId, null, null, 10, default))
            .Should().BeEmpty();

        var unsafeMetadata = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.SubordinateToRecorder,
            fields: [Field("Unsafe", "unsafe")]);
        Func<Task> unsafeColumn = async () => await unsafeMetadata.Reader.ListByRecorderDocumentAsync(
            RegisterId, RecorderId, null, null, 10, default);
        await unsafeColumn.Should().ThrowAsync<NgbConfigurationViolationException>();

    }

    [Fact]
    public async Task Key_history_covers_cursor_record_mode_periodicity_table_and_success_paths()
    {
        var independent = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            rows: RecordRows((1L, DimensionSetId, null, null, null, AsOf, false, null)));
        Func<Task> partialCursor = async () => await independent.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, null, null, AsOf, null, 10, default);
        Func<Task> invalidLimit = async () => await independent.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, null, null, null, null, 0, default);
        Func<Task> forbiddenRecorder = async () => await independent.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, null, RecorderId, null, null, 10, default);
        Func<Task> forbiddenPeriod = async () => await independent.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, null, null, null, 10, default);
        await partialCursor.Should().ThrowAsync<NgbArgumentInvalidException>();
        await invalidLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await forbiddenRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        await forbiddenPeriod.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        (await independent.Reader.ListKeyHistoryAsync(
            RegisterId, Guid.Empty, AsOf, null, null, null, null, 10, default)).Should().ContainSingle();

        var subordinate = Fixture(
            ReferenceRegisterPeriodicity.Month,
            ReferenceRegisterRecordMode.SubordinateToRecorder,
            fields: [Field("value_text", "value")],
            rows: RecordRows((2L, DimensionSetId, AsOf, AsOf.Date, RecorderId, AsOf, true, null)));
        Func<Task> requiredRecorder = async () => await subordinate.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, null, null, null, 10, default);
        Func<Task> emptyRecorder = async () => await subordinate.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, Guid.Empty, null, null, 10, default);
        Func<Task> requiredPeriod = async () => await subordinate.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, null, RecorderId, null, null, 10, default);
        await requiredRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        await emptyRecorder.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        await requiredPeriod.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        var rows = await subordinate.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, RecorderId, AsOf, 5, 10, default);
        rows.Should().ContainSingle();
        subordinate.Connection.Commands.Last().CommandText.Should()
            .Contain("period_bucket_utc = @PeriodBucketUtc")
            .And.Contain("@BeforeRecordedAtUtc, @BeforeRecordId");

        var noTable = Fixture(
            ReferenceRegisterPeriodicity.Month,
            ReferenceRegisterRecordMode.SubordinateToRecorder,
            tableExists: false);
        (await noTable.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, AsOf, RecorderId, null, null, 10, default)).Should().BeEmpty();

        var missingRegister = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent);
        missingRegister.Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        Func<Task> notFound = async () => await missingRegister.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, null, null, null, null, 10, default);
        await notFound.Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        var unsafeMetadata = Fixture(
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent,
            fields: [Field("Unsafe", "unsafe")]);
        Func<Task> unsafeColumn = async () => await unsafeMetadata.Reader.ListKeyHistoryAsync(
            RegisterId, DimensionSetId, AsOf, null, null, null, null, 10, default);
        await unsafeColumn.Should().ThrowAsync<NgbConfigurationViolationException>();

    }

    [Fact]
    public void Central_row_mapper_handles_null_dbnull_values_missing_fields_and_present_values()
    {
        var fields = new[] { Field("value_text", "value"), Field("missing_col", "missing") };
        var mappedNulls = PostgresReferenceRegisterRecordsReader.MapRow(
            new Dictionary<string, object?>
            {
                ["RecordId"] = 1L,
                ["DimensionSetId"] = DimensionSetId,
                ["PeriodUtc"] = null,
                ["PeriodBucketUtc"] = DBNull.Value,
                ["RecorderDocumentId"] = null,
                ["RecordedAtUtc"] = AsOf,
                ["IsDeleted"] = false,
                ["value_text"] = DBNull.Value
            },
            fields);
        mappedNulls.PeriodUtc.Should().BeNull();
        mappedNulls.PeriodBucketUtc.Should().BeNull();
        mappedNulls.RecorderDocumentId.Should().BeNull();
        mappedNulls.Values.Should().Contain("value", null).And.Contain("missing", null);

        var mappedDbNullGuid = PostgresReferenceRegisterRecordsReader.MapRow(
            new Dictionary<string, object?>
            {
                ["RecordId"] = 3L,
                ["DimensionSetId"] = DimensionSetId,
                ["PeriodUtc"] = null,
                ["PeriodBucketUtc"] = null,
                ["RecorderDocumentId"] = DBNull.Value,
                ["RecordedAtUtc"] = AsOf,
                ["IsDeleted"] = false
            },
            []);
        mappedDbNullGuid.RecorderDocumentId.Should().BeNull();

        var mappedValues = PostgresReferenceRegisterRecordsReader.MapRow(
            new Dictionary<string, object?>
            {
                ["RecordId"] = 2,
                ["DimensionSetId"] = DimensionSetId,
                ["PeriodUtc"] = AsOf,
                ["PeriodBucketUtc"] = AsOf.Date,
                ["RecorderDocumentId"] = RecorderId,
                ["RecordedAtUtc"] = AsOf,
                ["IsDeleted"] = true,
                ["value_text"] = "shown",
                ["missing_col"] = 42
            },
            fields);
        mappedValues.PeriodUtc.Should().Be(AsOf);
        mappedValues.PeriodBucketUtc.Should().Be(AsOf.Date);
        mappedValues.RecorderDocumentId.Should().Be(RecorderId);
        mappedValues.Values.Should().Contain("value", "shown").And.Contain("missing", 42);
    }

    private static ReaderFixture Fixture(
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode mode,
        bool tableExists = true,
        IReadOnlyList<ReferenceRegisterField>? fields = null,
        DataTable? rows = null)
        => new(periodicity, mode, tableExists, fields ?? [], rows ?? RecordRows());

    private static ReferenceRegisterField Field(string columnCode, string codeNorm)
        => new(RegisterId, codeNorm, codeNorm, columnCode, codeNorm, 0, ColumnType.String, true, AsOf, AsOf);

    private static DataTable RecordRows(
        params (long RecordId, Guid DimensionSetId, DateTime? PeriodUtc, DateTime? BucketUtc,
            Guid? RecorderId, DateTime RecordedAtUtc, bool IsDeleted, object? Value)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("RecordId", typeof(long));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("PeriodUtc", typeof(DateTime));
        table.Columns.Add("PeriodBucketUtc", typeof(DateTime));
        table.Columns.Add("RecorderDocumentId", typeof(Guid));
        table.Columns.Add("RecordedAtUtc", typeof(DateTime));
        table.Columns.Add("IsDeleted", typeof(bool));
        table.Columns.Add("value_text", typeof(object));
        foreach (var row in rows)
        {
            table.Rows.Add(
                row.RecordId,
                row.DimensionSetId,
                row.PeriodUtc ?? (object)DBNull.Value,
                row.BucketUtc ?? (object)DBNull.Value,
                row.RecorderId ?? (object)DBNull.Value,
                row.RecordedAtUtc,
                row.IsDeleted,
                row.Value ?? (object)DBNull.Value);
        }

        return table;
    }

    private sealed class ReaderFixture(
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode mode,
        bool tableExists,
        IReadOnlyList<ReferenceRegisterField> fields,
        DataTable rows)
    {
        public Mock<IReferenceRegisterRepository> Registers { get; } = CreateRegisters(periodicity, mode);
        public Mock<IReferenceRegisterFieldRepository> Fields { get; } = CreateFields(fields);
        public RecordingDbConnection Connection { get; } = new(
            _ => rows.CreateDataReader(),
            scalar: _ => tableExists);

        public PostgresReferenceRegisterRecordsReader Reader => new(
            new RecordingUnitOfWork(Connection), Registers.Object, Fields.Object);

        private static Mock<IReferenceRegisterRepository> CreateRegisters(
            ReferenceRegisterPeriodicity periodicity,
            ReferenceRegisterRecordMode mode)
        {
            var mock = new Mock<IReferenceRegisterRepository>(MockBehavior.Loose);
            mock.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new ReferenceRegisterAdminItem(
                    id, "Prices", "prices", "prices", "Prices", periodicity, mode, false, AsOf, AsOf));
            return mock;
        }

        private static Mock<IReferenceRegisterFieldRepository> CreateFields(
            IReadOnlyList<ReferenceRegisterField> fields)
        {
            var mock = new Mock<IReferenceRegisterFieldRepository>(MockBehavior.Loose);
            mock.Setup(x => x.GetByRegisterIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(fields);
            return mock;
        }
    }
}
