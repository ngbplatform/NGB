using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_MirrorMaterialization_GenericP0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ComputeFunction_MatchesCSharpDeterministicAlgorithm()
    {
        const string relationshipCodeNorm = "created_from";
        var fromDocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var toDocumentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var expected = DeterministicGuid.Create($"DocumentRelationship|{fromDocumentId:D}|{relationshipCodeNorm}|{toDocumentId:D}");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var actual = await conn.ExecuteScalarAsync<Guid>(
            "select ngb_compute_document_relationship_id(@fromDocumentId, @relationshipCodeNorm, @toDocumentId);",
            new { fromDocumentId, relationshipCodeNorm, toDocumentId });

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task GenericMirrorTrigger_InsertUpdateNullDelete_KeepsDocumentRelationshipsInSync()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var sourceDocumentId = Guid.CreateVersion7();
        var target1DocumentId = Guid.CreateVersion7();
        var target2DocumentId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await CreateTestTableAndInstallTriggerAsync(uow);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await docs.CreateAsync(new DocumentRecord
            {
                Id = sourceDocumentId,
                TypeCode = "it_source",
                Number = "SRC-0001",
                DateUtc = new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = target1DocumentId,
                TypeCode = "it_target",
                Number = "TGT-0001",
                DateUtc = new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = target2DocumentId,
                TypeCode = "it_target",
                Number = "TGT-0002",
                DateUtc = new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "insert into doc_it_mirror_source(document_id, target_document_id) values (@documentId, @targetDocumentId);",
                new { documentId = sourceDocumentId, targetDocumentId = target1DocumentId },
                uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        var rows = await LoadRowsAsync(Fixture.ConnectionString, sourceDocumentId);
        rows.Should().ContainSingle();
        rows[0].FromDocumentId.Should().Be(sourceDocumentId);
        rows[0].ToDocumentId.Should().Be(target1DocumentId);
        rows[0].RelationshipCode.Should().Be("created_from");

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "update doc_it_mirror_source set target_document_id = @targetDocumentId where document_id = @documentId;",
                new { documentId = sourceDocumentId, targetDocumentId = target2DocumentId },
                uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        rows = await LoadRowsAsync(Fixture.ConnectionString, sourceDocumentId);
        rows.Should().ContainSingle();
        rows[0].ToDocumentId.Should().Be(target2DocumentId);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "update doc_it_mirror_source set target_document_id = @targetDocumentId where document_id = @documentId;",
                new { documentId = sourceDocumentId, targetDocumentId = target2DocumentId },
                uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        rows = await LoadRowsAsync(Fixture.ConnectionString, sourceDocumentId);
        rows.Should().ContainSingle("idempotent repeated updates must not duplicate mirrored edges");
        rows[0].ToDocumentId.Should().Be(target2DocumentId);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "update doc_it_mirror_source set target_document_id = null where document_id = @documentId;",
                new { documentId = sourceDocumentId },
                uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        rows = await LoadRowsAsync(Fixture.ConnectionString, sourceDocumentId);
        rows.Should().BeEmpty();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "update doc_it_mirror_source set target_document_id = @targetDocumentId where document_id = @documentId;",
                new { documentId = sourceDocumentId, targetDocumentId = target2DocumentId },
                uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        rows = await LoadRowsAsync(Fixture.ConnectionString, sourceDocumentId);
        rows.Should().ContainSingle();
        rows[0].ToDocumentId.Should().Be(target2DocumentId);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "delete from doc_it_mirror_source where document_id = @documentId;",
                new { documentId = sourceDocumentId },
                uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        rows = await LoadRowsAsync(Fixture.ConnectionString, sourceDocumentId);
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Installer_WhenMirroredColumnIsNotUuid_ThrowsHelpfulError()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            "create table doc_it_bad_mirror (document_id uuid primary key references documents(id) on delete cascade, bad_ref text null);");

        Func<Task> act = () => conn.ExecuteAsync(
            "select ngb_install_mirrored_document_relationship_trigger(@tableName, @columnName, @relationshipCode);",
            new { tableName = "doc_it_bad_mirror", columnName = "bad_ref", relationshipCode = "created_from" });

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("42804");
        ex.Which.MessageText.Should().Contain("must be uuid");
    }

    private static async Task CreateTestTableAndInstallTriggerAsync(IUnitOfWork uow)
    {
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "create table doc_it_mirror_source (document_id uuid primary key references documents(id) on delete cascade, target_document_id uuid null);",
                transaction: uow.Transaction,
                cancellationToken: ct));

            await uow.Connection.ExecuteAsync(new CommandDefinition(
                "select ngb_install_mirrored_document_relationship_trigger(@tableName, @columnName, @relationshipCode);",
                new { tableName = "doc_it_mirror_source", columnName = "target_document_id", relationshipCode = "created_from" },
                uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<RelRow>> LoadRowsAsync(string connectionString, Guid fromDocumentId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var rows = await conn.QueryAsync<RelRow>(
            "select from_document_id as \"FromDocumentId\", to_document_id as \"ToDocumentId\", relationship_code as \"RelationshipCode\" from document_relationships where from_document_id = @fromDocumentId order by created_at_utc desc, relationship_id desc;",
            new { fromDocumentId });

        return rows.AsList();
    }

    private sealed record RelRow(Guid FromDocumentId, Guid ToDocumentId, string RelationshipCode);
}
