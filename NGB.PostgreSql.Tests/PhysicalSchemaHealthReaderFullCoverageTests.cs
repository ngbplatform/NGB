using System.Data;
using FluentAssertions;
using Moq;
using NGB.Metadata.Documents.Storage;
using NGB.Metadata.Schema;
using NGB.Persistence.Schema;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.ReferenceRegisters;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class PhysicalSchemaHealthReaderFullCoverageTests
{
    [Fact]
    public async Task Document_relationship_health_reports_every_required_artifact_when_table_is_absent()
    {
        var inspector = new Mock<IDbSchemaInspector>(MockBehavior.Strict);
        inspector.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptySnapshot());
        var documentTypes = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        documentTypes.Setup(x => x.GetAll()).Returns([]);
        var sut = new PostgresDocumentRelationshipsPhysicalSchemaHealthReader(
            inspector.Object,
            new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => 0)),
            documentTypes.Object);

        var health = await sut.GetAsync();

        health.Exists.Should().BeFalse();
        health.MissingColumns.Should().HaveCount(6);
        health.MissingIndexes.Should().HaveCount(9);
        health.MissingConstraints.Should().HaveCount(7);
        health.HasDraftGuardTrigger.Should().BeFalse();
        health.HasDraftGuardFunction.Should().BeFalse();
        health.HasMirroringComputeFunction.Should().BeFalse();
        health.HasMirroringSyncFunction.Should().BeFalse();
        health.HasMirroringInstallerFunction.Should().BeFalse();
        health.MissingMirroredTriggerBindings.Should().BeEmpty();
        inspector.VerifyAll();
        documentTypes.VerifyAll();
    }

    [Fact]
    public async Task Document_relationship_health_covers_existing_schema_with_missing_and_present_artifacts()
    {
        const string tableName = "document_relationships";
        var documentTypes = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        documentTypes.Setup(x => x.GetAll()).Returns([]);

        var missingCollections = new Mock<IDbSchemaInspector>(MockBehavior.Strict);
        missingCollections.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new DbSchemaSnapshot(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tableName },
                new Dictionary<string, IReadOnlyList<DbColumnSchema>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<DbIndexSchema>>(StringComparer.OrdinalIgnoreCase)));
        var missing = new PostgresDocumentRelationshipsPhysicalSchemaHealthReader(
            missingCollections.Object,
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => ConstraintRows(),
                scalar: _ => 1)),
            documentTypes.Object);
        var missingHealth = await missing.GetAsync();
        missingHealth.Exists.Should().BeTrue();
        missingHealth.MissingColumns.Should().HaveCount(6);
        missingHealth.MissingIndexes.Should().HaveCount(9);
        missingHealth.MissingConstraints.Should().HaveCount(7);

        var partialCollections = new Mock<IDbSchemaInspector>(MockBehavior.Strict);
        partialCollections.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new DbSchemaSnapshot(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tableName },
                new Dictionary<string, IReadOnlyList<DbColumnSchema>>(StringComparer.OrdinalIgnoreCase)
                {
                    [tableName] = [new(tableName, "relationship_id", "uuid", false, null)]
                },
                new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<DbIndexSchema>>(StringComparer.OrdinalIgnoreCase)
                {
                    [tableName] = [new(tableName, "ix_docrel_from_created_id", [], false)]
                }));
        var partial = new PostgresDocumentRelationshipsPhysicalSchemaHealthReader(
            partialCollections.Object,
            new RecordingUnitOfWork(new RecordingDbConnection(
                readerFactory: _ => ConstraintRows("ck_document_relationships_code_trimmed"),
                scalar: _ => 1)),
            documentTypes.Object);
        var partialHealth = await partial.GetAsync();
        partialHealth.MissingColumns.Should().HaveCount(5);
        partialHealth.MissingIndexes.Should().HaveCount(8);
        partialHealth.MissingConstraints.Should().HaveCount(6);
    }

    [Fact]
    public async Task Reference_register_health_returns_empty_without_registers()
    {
        var inspector = new Mock<IDbSchemaInspector>(MockBehavior.Strict);
        var sut = new PostgresReferenceRegisterPhysicalSchemaHealthReader(
            inspector.Object,
            new RecordingUnitOfWork(new RecordingDbConnection(_ => EmptyRegisterRows())));

        var report = await sut.GetReportAsync();

        report.Items.Should().BeEmpty();
        inspector.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reference_register_health_builds_non_periodic_subordinate_indexes_for_absent_table()
    {
        var inspector = new Mock<IDbSchemaInspector>(MockBehavior.Strict);
        inspector.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptySnapshot());
        var registerId = Guid.NewGuid();
        var connection = new RecordingDbConnection(sql =>
        {
            if (sql.Contains("FROM reference_registers", StringComparison.Ordinal))
                return RegisterRows(registerId);
            if (sql.Contains("FROM reference_register_fields", StringComparison.Ordinal))
                return EmptyFieldRows();
            if (sql.Contains("FROM pg_trigger", StringComparison.Ordinal))
                return EmptyAppendOnlyRows();
            throw new InvalidOperationException($"Unexpected SQL: {sql}");
        });
        var sut = new PostgresReferenceRegisterPhysicalSchemaHealthReader(
            inspector.Object,
            new RecordingUnitOfWork(connection));

        var item = (await sut.GetReportAsync()).Items.Should().ContainSingle().Subject;

        item.Register.RegisterId.Should().Be(registerId);
        item.Records.Exists.Should().BeFalse();
        item.Records.HasAppendOnlyGuard.Should().BeNull();
        item.Records.MissingIndexes.Should().Contain(x =>
            x.Contains("index(recorder_document_id, dimension_set_id, recorded_at_utc, record_id)", StringComparison.Ordinal));
        inspector.VerifyAll();
    }

    [Fact]
    public async Task Reference_register_health_builds_periodic_independent_indexes_for_absent_table()
    {
        var inspector = new Mock<IDbSchemaInspector>(MockBehavior.Strict);
        inspector.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EmptySnapshot());
        var registerId = Guid.NewGuid();
        var connection = new RecordingDbConnection(sql =>
        {
            if (sql.Contains("FROM reference_registers", StringComparison.Ordinal))
                return RegisterRows(registerId, ReferenceRegisterPeriodicity.Day, ReferenceRegisterRecordMode.SubordinateToRecorder);
            if (sql.Contains("FROM reference_register_fields", StringComparison.Ordinal))
                return EmptyFieldRows();
            if (sql.Contains("FROM pg_trigger", StringComparison.Ordinal))
                return EmptyAppendOnlyRows();
            throw new InvalidOperationException($"Unexpected SQL: {sql}");
        });

        var item = (await new PostgresReferenceRegisterPhysicalSchemaHealthReader(
            inspector.Object,
            new RecordingUnitOfWork(connection)).GetReportAsync()).Items.Should().ContainSingle().Subject;

        item.Records.MissingIndexes.Should().Contain(x =>
            x.Contains("period_bucket_utc", StringComparison.Ordinal));
    }

    private static DbSchemaSnapshot EmptySnapshot() => new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<DbColumnSchema>>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<DbIndexSchema>>(StringComparer.OrdinalIgnoreCase));

    private static DataTableReader EmptyRegisterRows() => RegisterTable().CreateDataReader();

    private static DataTableReader RegisterRows(
        Guid registerId,
        ReferenceRegisterPeriodicity periodicity = ReferenceRegisterPeriodicity.NonPeriodic,
        ReferenceRegisterRecordMode mode = ReferenceRegisterRecordMode.SubordinateToRecorder)
    {
        var table = RegisterTable();
        table.Rows.Add(
            registerId,
            "Prices",
            "prices",
            "prices",
            "Prices",
            (short)periodicity,
            (short)mode,
            false,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch);
        return table.CreateDataReader();
    }

    private static DataTable RegisterTable()
    {
        var table = new DataTable();
        table.Columns.Add("RegisterId", typeof(Guid));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("CodeNorm", typeof(string));
        table.Columns.Add("TableCode", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Periodicity", typeof(short));
        table.Columns.Add("RecordMode", typeof(short));
        table.Columns.Add("HasRecords", typeof(bool));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        return table;
    }

    private static DataTableReader EmptyFieldRows()
    {
        var table = new DataTable();
        table.Columns.Add("RegisterId", typeof(Guid));
        table.Columns.Add("ColumnCode", typeof(string));
        return table.CreateDataReader();
    }

    private static DataTableReader EmptyAppendOnlyRows()
    {
        var table = new DataTable();
        table.Columns.Add("TableName", typeof(string));
        table.Columns.Add("HasGuard", typeof(bool));
        return table.CreateDataReader();
    }

    private static DataTableReader ConstraintRows(params string[] names)
    {
        var table = new DataTable();
        table.Columns.Add("conname", typeof(string));
        foreach (var name in names)
            table.Rows.Add(name);
        return table.CreateDataReader();
    }
}
