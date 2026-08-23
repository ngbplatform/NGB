using System.Data;
using FluentAssertions;
using NGB.Core.Catalogs;
using NGB.Core.Catalogs.Exceptions;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Reporting;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Reporting;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class RepositoriesFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Catalog_create_many_validates_null_empty_and_each_catalog_code()
    {
        var connection = new RecordingDbConnection();
        var sut = CatalogRepository(connection);

        Func<Task> nullBatch = () => sut.CreateManyAsync(null!);
        await nullBatch.Should().ThrowAsync<NgbArgumentRequiredException>()
            .Where(x => Equals(x.Context["paramName"], "catalogs"));

        await sut.CreateManyAsync([]);
        connection.Commands.Should().BeEmpty();

        Func<Task> blankCode = () => sut.CreateManyAsync([Catalog(" \t")]);
        await blankCode.Should().ThrowAsync<NgbArgumentRequiredException>()
            .Where(x => Equals(x.Context["paramName"], "catalogs"));

        await sut.CreateManyAsync([Catalog(" first "), Catalog("second")]);
        connection.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task Catalog_state_updates_distinguish_missing_success_and_impossible_row_counts()
    {
        var id = Guid.NewGuid();

        foreach (var operation in CatalogOperations(CatalogRepository(new RecordingDbConnection(nonQuery: _ => 0)), id))
            await operation.Should().ThrowAsync<CatalogNotFoundException>()
                .Where(x => x.CatalogId == id);

        var successfulConnection = new RecordingDbConnection(nonQuery: _ => 1);
        foreach (var operation in CatalogOperations(CatalogRepository(successfulConnection), id))
            await operation();
        successfulConnection.Commands.Should().HaveCount(3);

        foreach (var operation in CatalogOperations(CatalogRepository(new RecordingDbConnection(nonQuery: _ => 2)), id))
        {
            var error = await operation.Should().ThrowAsync<NgbInvariantViolationException>();
            error.Which.Context.Should().Contain("catalogId", id).And.Contain("rows", 2);
        }
    }

    [Fact]
    public async Task Document_repository_validates_required_values_and_missing_increment_target()
    {
        var invalid = DocumentRepository(new RecordingDbConnection());
        Func<Task> blankType = () => invalid.CreateAsync(Document("\n"));
        Func<Task> blankNumber = () => invalid.TrySetNumberAsync(Guid.NewGuid(), " ", Now);

        await blankType.Should().ThrowAsync<NgbArgumentRequiredException>()
            .Where(x => Equals(x.Context["paramName"], "doc"));
        await blankNumber.Should().ThrowAsync<NgbArgumentRequiredException>()
            .Where(x => Equals(x.Context["paramName"], "number"));

        var missing = DocumentRepository(new RecordingDbConnection(readerFactory: _ => EmptyDocumentRows()));
        var id = Guid.NewGuid();
        Func<Task> increment = () => missing.IncrementVersionAsync(id, Now);
        await increment.Should().ThrowAsync<DocumentNotFoundException>()
            .Where(x => x.DocumentId == id);
    }

    [Fact]
    public async Task Document_writes_reject_impossible_multi_row_results()
    {
        var connection = new RecordingDbConnection(nonQuery: _ => 2);
        var sut = DocumentRepository(connection);
        var id = Guid.NewGuid();

        Func<Task>[] operations =
        [
            () => sut.UpdateStatusAsync(id, DocumentStatus.Posted, Now, Now, null),
            () => sut.TrySetNumberAsync(id, "  INV-1  ", Now),
            () => sut.UpdateDraftHeaderAsync(id, "  INV-2  ", Now, Now),
            () => sut.TryDeleteAsync(id)
        ];

        foreach (var operation in operations)
        {
            var error = await operation.Should().ThrowAsync<NgbInvariantViolationException>();
            error.Which.Context.Should().Contain("documentId", id).And.Contain("rows", 2);
        }

        var successId = Guid.NewGuid();
        var success = DocumentRepository(new RecordingDbConnection(
            readerFactory: _ => DocumentRows(successId),
            nonQuery: _ => 1));
        await success.CreateAsync(Document("invoice"));
        await success.UpdateStatusAsync(successId, DocumentStatus.Posted, Now, Now, null);
        (await success.IncrementVersionAsync(successId, Now)).Id.Should().Be(successId);
        (await success.TrySetNumberAsync(successId, "INV-1", Now)).Should().BeTrue();
        (await success.TryDeleteAsync(successId)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("  INV-3  ")]
    public async Task Update_draft_header_normalizes_optional_number_and_preserves_boundary_dates(string? number)
    {
        var connection = new RecordingDbConnection(nonQuery: _ => 1);
        var sut = DocumentRepository(connection);

        (await sut.UpdateDraftHeaderAsync(Guid.NewGuid(), number, DateTime.MinValue.ToUniversalTime(), DateTime.MaxValue.ToUniversalTime()))
            .Should().BeTrue();

        var numberParameter = connection.Commands.Single().ParametersSnapshot.Single(x => x.ParameterName == "Number");
        numberParameter.Value.Should().Be(number is null || string.IsNullOrWhiteSpace(number) ? DBNull.Value : "INV-3");
    }

    [Fact]
    public async Task Report_variant_repository_validates_every_required_argument_before_database_access()
    {
        var connection = new RecordingDbConnection();
        var sut = ReportRepository(connection);

        Func<Task>[] reportCodeCases =
        [
            () => sut.ListVisibleAsync(" ", null, default),
            () => sut.GetVisibleAsync(" ", "variant", null, default),
            () => sut.ListByCodeAsync(" ", "variant", default),
            () => sut.ClearDefaultAsync(" ", null, true, null, default),
            () => sut.DeleteVisibleAsync(" ", "variant", null, default)
        ];
        foreach (var operation in reportCodeCases)
            await operation.Should().ThrowAsync<NgbArgumentRequiredException>()
                .Where(x => Equals(x.Context["paramName"], "reportCodeNorm"));

        Func<Task>[] variantCodeCases =
        [
            () => sut.GetVisibleAsync("report", "\t", null, default),
            () => sut.ListByCodeAsync("report", "\t", default),
            () => sut.DeleteVisibleAsync("report", "\t", null, default)
        ];
        foreach (var operation in variantCodeCases)
            await operation.Should().ThrowAsync<NgbArgumentRequiredException>()
                .Where(x => Equals(x.Context["paramName"], "variantCodeNorm"));

        Func<Task> nullRecord = () => sut.UpsertAsync(null!, default);
        await nullRecord.Should().ThrowAsync<NgbArgumentRequiredException>()
            .Where(x => Equals(x.Context["paramName"], "record"));
        connection.Commands.Should().BeEmpty();

        var record = ReportVariant();
        var positive = ReportRepository(new RecordingDbConnection(
            readerFactory: sql => sql.Contains("RETURNING", StringComparison.Ordinal)
                ? ReportVariantRows(record)
                : new DataTable().CreateDataReader()));
        (await positive.ListVisibleAsync("report", null, default)).Should().BeEmpty();
        (await positive.GetVisibleAsync("report", "variant", null, default)).Should().BeNull();
        (await positive.ListByCodeAsync("report", "variant", default)).Should().BeEmpty();
        (await positive.UpsertAsync(record, default)).Should().Be(record);
        (await positive.DeleteVisibleAsync("report", "variant", null, default)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("  keep-me  ", "keep-me")]
    public async Task Clear_default_normalizes_optional_exception_code(string? supplied, string? expected)
    {
        var connection = new RecordingDbConnection();
        var sut = ReportRepository(connection);

        await sut.ClearDefaultAsync("  report  ", Guid.NewGuid(), false, supplied, default);

        var command = connection.Commands.Single();
        command.ParametersSnapshot.Single(x => x.ParameterName == "ReportCodeNorm").Value.Should().Be("report");
        command.ParametersSnapshot.Single(x => x.ParameterName == "ExceptVariantCodeNorm").Value
            .Should().Be(expected is null ? DBNull.Value : expected);
    }

    private static IEnumerable<Func<Task>> CatalogOperations(PostgresCatalogRepository repository, Guid id)
    {
        yield return () => repository.MarkForDeletionAsync(id, Now);
        yield return () => repository.UnmarkForDeletionAsync(id, Now);
        yield return () => repository.TouchAsync(id, Now);
    }

    private static PostgresCatalogRepository CatalogRepository(RecordingDbConnection connection)
        => new(new RecordingUnitOfWork(connection, hasActiveTransaction: true));

    private static PostgresDocumentRepository DocumentRepository(RecordingDbConnection connection)
        => new(new RecordingUnitOfWork(connection, hasActiveTransaction: true));

    private static PostgresReportVariantRepository ReportRepository(RecordingDbConnection connection)
        => new(new RecordingUnitOfWork(connection, hasActiveTransaction: true), TimeProvider.System);

    private static CatalogRecord Catalog(string code) => new()
    {
        Id = Guid.NewGuid(),
        CatalogCode = code,
        IsDeleted = false,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private static DocumentRecord Document(string typeCode) => new()
    {
        Id = Guid.NewGuid(),
        TypeCode = typeCode,
        DateUtc = Now,
        Status = DocumentStatus.Draft,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private static ReportVariantRecord ReportVariant() => new(
        Guid.NewGuid(), "report", "report", "variant", "variant", null, "Variant",
        null, null, null, false, true, Now, Now);

    private static DataTableReader DocumentRows(Guid id)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("TypeCode", typeof(string));
        table.Columns.Add("Number", typeof(string));
        table.Columns.Add("DateUtc", typeof(DateTime));
        table.Columns.Add("Status", typeof(short));
        table.Columns.Add("Version", typeof(long));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        table.Columns.Add("PostedAtUtc", typeof(DateTime));
        table.Columns.Add("MarkedForDeletionAtUtc", typeof(DateTime));
        table.Rows.Add(id, "invoice", DBNull.Value, Now, (short)DocumentStatus.Draft, 1L, Now, Now, DBNull.Value, DBNull.Value);
        return table.CreateDataReader();
    }

    private static DataTableReader ReportVariantRows(ReportVariantRecord record)
    {
        var table = new DataTable();
        table.Columns.Add("ReportVariantId", typeof(Guid));
        table.Columns.Add("ReportCode", typeof(string));
        table.Columns.Add("ReportCodeNorm", typeof(string));
        table.Columns.Add("VariantCode", typeof(string));
        table.Columns.Add("VariantCodeNorm", typeof(string));
        table.Columns.Add("OwnerPlatformUserId", typeof(Guid));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("LayoutJson", typeof(string));
        table.Columns.Add("FiltersJson", typeof(string));
        table.Columns.Add("ParametersJson", typeof(string));
        table.Columns.Add("IsDefault", typeof(bool));
        table.Columns.Add("IsShared", typeof(bool));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        table.Rows.Add(
            record.ReportVariantId, record.ReportCode, record.ReportCodeNorm, record.VariantCode,
            record.VariantCodeNorm, DBNull.Value, record.Name, DBNull.Value, DBNull.Value, DBNull.Value,
            record.IsDefault, record.IsShared, record.CreatedAtUtc, record.UpdatedAtUtc);
        return table.CreateDataReader();
    }

    private static DataTableReader EmptyDocumentRows()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        return table.CreateDataReader();
    }
}
