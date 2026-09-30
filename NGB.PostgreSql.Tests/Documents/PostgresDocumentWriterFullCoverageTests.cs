using FluentAssertions;
using NGB.Metadata.Base;
using NGB.Persistence.Documents.Universal;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Documents;

public sealed class PostgresDocumentWriterFullCoverageTests
{
    [Fact]
    public async Task Upsert_head_validates_id_values_column_and_table_identifiers()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresDocumentWriter(new RecordingUnitOfWork(connection, hasActiveTransaction: true));

        Func<Task> emptyId = () => sut.UpsertHeadAsync(Head("doc_invoice"), Guid.Empty, []);
        Func<Task> nullValues = () => sut.UpsertHeadAsync(Head("doc_invoice"), Guid.NewGuid(), null!);
        Func<Task> blankColumn = () => sut.UpsertHeadAsync(
            Head("doc_invoice"), Guid.NewGuid(), [new(" ", ColumnType.String, "value")]);
        Func<Task> blankTable = () => sut.UpsertHeadAsync(
            Head(" "), Guid.NewGuid(), [new("value", ColumnType.String, "value")]);

        await emptyId.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullValues.Should().ThrowAsync<NgbArgumentRequiredException>();
        await blankColumn.Should().ThrowAsync<NgbArgumentInvalidException>();
        await blankTable.Should().ThrowAsync<NgbArgumentInvalidException>();

        await sut.UpsertHeadAsync(Head("doc_invoice"), Guid.NewGuid(), []);
        connection.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_head_quotes_identifiers_and_handles_json_and_scalar_values()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresDocumentWriter(new RecordingUnitOfWork(connection, hasActiveTransaction: true));
        var id = Guid.NewGuid();

        await sut.UpsertHeadAsync(
            Head("doc_\"invoice"),
            id,
            [
                new("payload", ColumnType.Json, "{}"),
                new("display\"name", ColumnType.String, "Invoice")
            ]);

        var command = connection.Commands.Single();
        command.CommandText.Should().Contain("INSERT INTO \"doc_\"\"invoice\"");
        command.CommandText.Should().Contain("CAST(@payload AS jsonb)");
        command.CommandText.Should().Contain("\"display\"\"name\" = @display\"name");
    }

    private static DocumentHeadDescriptor Head(string table)
        => new("invoice", table, "display", []);
}
