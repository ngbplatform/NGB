using FluentAssertions;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class HeadTypeStorageFullCoverageTests
{
    [Fact]
    public async Task Document_head_storage_validates_configuration_and_executes_empty_and_populated_commands()
    {
        var inactive = new RecordingUnitOfWork(new RecordingDbConnection());
        AssertDocumentRequired(null!, "type", "doc_type", []);
        AssertDocumentRequired(inactive, " ", "doc_type", []);
        AssertDocumentRequired(inactive, "type", "doc_type", null!);
        AssertDocumentRequired(inactive, "type", "doc_type", [null!]);
        AssertDocumentRequired(inactive, "type", "doc_type",
            [new("value", "value", null!)]);
        AssertDocumentInvalid(inactive, "Unsafe", []);
        AssertDocumentInvalid(inactive, "doc_type", [new("Unsafe", "value", _ => 1)]);
        AssertDocumentInvalid(inactive, "doc_type", [new("value", " ", _ => 1)]);
        AssertDocumentInvalid(inactive, "doc_type", [new("value", "Unsafe", _ => 1)]);

        var inactiveStorage = new PostgresHeadDocumentTypeStorage(inactive, "type", "doc_type", []);
        Func<Task> inactiveCreate = () => inactiveStorage.CreateDraftAsync(Guid.NewGuid(), default);
        Func<Task> inactiveBatchCreate = () => inactiveStorage.CreateDraftsAsync([], default);
        Func<Task> inactiveDelete = () => inactiveStorage.DeleteDraftAsync(Guid.NewGuid(), default);
        await inactiveCreate.Should().ThrowAsync<InvalidOperationException>();
        await inactiveBatchCreate.Should().ThrowAsync<InvalidOperationException>();
        await inactiveDelete.Should().ThrowAsync<InvalidOperationException>();

        var connection = new RecordingDbConnection();
        var active = new RecordingUnitOfWork(connection, hasActiveTransaction: true);
        var empty = new PostgresHeadDocumentTypeStorage(active, "empty", "doc_empty", []);
        empty.TypeCode.Should().Be("empty");
        var emptyId = Guid.NewGuid();
        await empty.CreateDraftAsync(emptyId, default);
        await empty.DeleteDraftAsync(emptyId, default);
        connection.Commands[0].CommandText.Should().Be(
            "INSERT INTO doc_empty(document_id) VALUES (@documentId) ON CONFLICT (document_id) DO NOTHING;");
        connection.Commands[1].CommandText.Should().Be(
            "DELETE FROM doc_empty WHERE document_id = @documentId;");

        var column = PostgresHeadDocumentTypeStorage.Column.DraftString(
            "display", "display", prefix: "draft/", guidFormat: "D");
        column.ColumnName.Should().Be("display");
        column.ParameterName.Should().Be("display");
        var populated = new PostgresHeadDocumentTypeStorage(active, "type", "doc_type", [column]);
        var id = Guid.NewGuid();
        column.ValueFactory(id).Should().Be($"draft/{id:D}");
        PostgresHeadDocumentTypeStorage.Column.DraftString("code", "code").ValueFactory(id)
            .Should().Be($"DRAFT-{id:N}");
        await populated.CreateDraftAsync(id, default);
        connection.Commands[2].CommandText.Should().Be(
            "INSERT INTO doc_type(document_id, display) VALUES (@documentId, @display) ON CONFLICT (document_id) DO NOTHING;");
        connection.Commands[2].ParametersSnapshot.Should().Contain(x =>
            x.ParameterName == "display" && Equals(x.Value, $"draft/{id:D}"));

        Func<Task> nullBatch = () => populated.CreateDraftsAsync(null!, default);
        await nullBatch.Should().ThrowAsync<ArgumentNullException>();
        await populated.CreateDraftsAsync([], default);
        connection.Commands.Should().HaveCount(3);

        var batchIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await populated.CreateDraftsAsync(batchIds, default);
        connection.Commands[3].CommandText.Should().Be(
            "INSERT INTO doc_type(document_id, display) VALUES (@documentId_0, @display_0), (@documentId_1, @display_1) ON CONFLICT (document_id) DO NOTHING;");
        connection.Commands[3].ParametersSnapshot.Should().Contain(x =>
            x.ParameterName == "display_0" && Equals(x.Value, $"draft/{batchIds[0]:D}"));
        connection.Commands[3].ParametersSnapshot.Should().Contain(x =>
            x.ParameterName == "display_1" && Equals(x.Value, $"draft/{batchIds[1]:D}"));

        await empty.CreateDraftsAsync(batchIds, default);
        connection.Commands[4].CommandText.Should().Be(
            "INSERT INTO doc_empty(document_id) VALUES (@documentId_0), (@documentId_1) ON CONFLICT (document_id) DO NOTHING;");
    }

    [Fact]
    public async Task Catalog_head_storage_validates_configuration_and_executes_empty_and_populated_commands()
    {
        var inactive = new RecordingUnitOfWork(new RecordingDbConnection());
        AssertCatalogRequired(null!, "catalog", "cat_type", []);
        AssertCatalogRequired(inactive, " ", "cat_type", []);
        AssertCatalogRequired(inactive, "catalog", "cat_type", null!);
        AssertCatalogRequired(inactive, "catalog", "cat_type", [null!]);
        AssertCatalogRequired(inactive, "catalog", "cat_type",
            [new("value", "value", null!)]);
        AssertCatalogInvalid(inactive, "Unsafe", []);
        AssertCatalogInvalid(inactive, "cat_type", [new("Unsafe", "value", _ => 1)]);
        AssertCatalogInvalid(inactive, "cat_type", [new("value", " ", _ => 1)]);
        AssertCatalogInvalid(inactive, "cat_type", [new("value", "Unsafe", _ => 1)]);

        var inactiveStorage = new PostgresHeadCatalogTypeStorage(inactive, "catalog", "cat_type", []);
        Func<Task> inactiveCreate = () => inactiveStorage.EnsureCreatedAsync(Guid.NewGuid(), default);
        Func<Task> inactiveDelete = () => inactiveStorage.DeleteAsync(Guid.NewGuid(), default);
        await inactiveCreate.Should().ThrowAsync<InvalidOperationException>();
        await inactiveDelete.Should().ThrowAsync<InvalidOperationException>();

        var connection = new RecordingDbConnection();
        var active = new RecordingUnitOfWork(connection, hasActiveTransaction: true);
        var empty = new PostgresHeadCatalogTypeStorage(active, "empty", "cat_empty", []);
        empty.CatalogCode.Should().Be("empty");
        var emptyId = Guid.NewGuid();
        await empty.EnsureCreatedAsync(emptyId, default);
        await empty.DeleteAsync(emptyId, default);
        connection.Commands[0].CommandText.Should().Be(
            "INSERT INTO cat_empty(catalog_id) VALUES (@catalogId) ON CONFLICT (catalog_id) DO NOTHING;");
        connection.Commands[1].CommandText.Should().Be(
            "DELETE FROM cat_empty WHERE catalog_id = @catalogId;");

        var column = PostgresHeadCatalogTypeStorage.Column.DraftString(
            "display", "display", prefix: "draft/", guidFormat: "D");
        column.ColumnName.Should().Be("display");
        column.ParameterName.Should().Be("display");
        var populated = new PostgresHeadCatalogTypeStorage(active, "catalog", "cat_type", [column]);
        var id = Guid.NewGuid();
        column.ValueFactory(id).Should().Be($"draft/{id:D}");
        PostgresHeadCatalogTypeStorage.Column.DraftString("code", "code").ValueFactory(id)
            .Should().Be($"DRAFT-{id:N}");
        await populated.EnsureCreatedAsync(id, default);
        connection.Commands[2].CommandText.Should().Be(
            "INSERT INTO cat_type(catalog_id, display) VALUES (@catalogId, @display) ON CONFLICT (catalog_id) DO NOTHING;");
        connection.Commands[2].ParametersSnapshot.Should().Contain(x =>
            x.ParameterName == "display" && Equals(x.Value, $"draft/{id:D}"));
    }

    private static void AssertDocumentRequired(
        RecordingUnitOfWork uow,
        string typeCode,
        string table,
        IReadOnlyList<PostgresHeadDocumentTypeStorage.Column> columns)
    {
        Action act = () => new PostgresHeadDocumentTypeStorage(uow, typeCode, table, columns);
        act.Should().Throw<NgbArgumentRequiredException>();
    }

    private static void AssertDocumentInvalid(
        RecordingUnitOfWork uow,
        string table,
        IReadOnlyList<PostgresHeadDocumentTypeStorage.Column> columns)
    {
        Action act = () => new PostgresHeadDocumentTypeStorage(uow, "type", table, columns);
        act.Should().Throw<NgbConfigurationViolationException>();
    }

    private static void AssertCatalogRequired(
        RecordingUnitOfWork uow,
        string catalogCode,
        string table,
        IReadOnlyList<PostgresHeadCatalogTypeStorage.Column> columns)
    {
        Action act = () => new PostgresHeadCatalogTypeStorage(uow, catalogCode, table, columns);
        act.Should().Throw<NgbArgumentRequiredException>();
    }

    private static void AssertCatalogInvalid(
        RecordingUnitOfWork uow,
        string table,
        IReadOnlyList<PostgresHeadCatalogTypeStorage.Column> columns)
    {
        Action act = () => new PostgresHeadCatalogTypeStorage(uow, "catalog", table, columns);
        act.Should().Throw<NgbConfigurationViolationException>();
    }
}
