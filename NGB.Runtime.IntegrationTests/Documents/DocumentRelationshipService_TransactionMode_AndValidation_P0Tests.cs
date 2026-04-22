using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationshipService_TransactionMode_AndValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAsync_WhenFromIdIsEmpty_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        Func<Task> act = () => svc.CreateAsync(
            fromDocumentId: Guid.Empty,
            toDocumentId: Guid.CreateVersion7(),
            relationshipCode: "based_on",
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("fromDocumentId");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Fact]
    public async Task CreateAsync_WhenToIdIsEmpty_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        Func<Task> act = () => svc.CreateAsync(
            fromDocumentId: Guid.CreateVersion7(),
            toDocumentId: Guid.Empty,
            relationshipCode: "based_on",
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("toDocumentId");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Fact]
    public async Task CreateAsync_WhenIdsAreEqual_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        var id = Guid.CreateVersion7();

        Func<Task> act = () => svc.CreateAsync(
            fromDocumentId: id,
            toDocumentId: id,
            relationshipCode: "based_on",
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("fromDocumentId");
        ex.Which.Reason.Should().Contain("must be different");
        ex.Which.AssertNgbError(NgbArgumentInvalidException.Code, "paramName", "reason");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task CreateAsync_WhenRelationshipCodeBlank_Throws(string code)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        Func<Task> act = () => svc.CreateAsync(
            fromDocumentId: Guid.CreateVersion7(),
            toDocumentId: Guid.CreateVersion7(),
            relationshipCode: code,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("relationshipCode");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task DeleteAsync_WhenRelationshipCodeBlank_Throws(string code)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        Func<Task> act = () => svc.DeleteAsync(
            fromDocumentId: Guid.CreateVersion7(),
            toDocumentId: Guid.CreateVersion7(),
            relationshipCode: code,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("relationshipCode");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Fact]
    public async Task ListOutgoingAsync_WhenFromIdIsEmpty_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        Func<Task> act = () => svc.ListOutgoingAsync(Guid.Empty, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("fromDocumentId");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Fact]
    public async Task ListIncomingAsync_WhenToIdIsEmpty_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        Func<Task> act = () => svc.ListIncomingAsync(Guid.Empty, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("toDocumentId");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Fact]
    public async Task CreateAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        Func<Task> act = () => svc.CreateAsync(
            fromDocumentId: Guid.CreateVersion7(),
            toDocumentId: Guid.CreateVersion7(),
            relationshipCode: "based_on",
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task CreateAsync_ManageTransactionFalse_ExternalCommit_Persists_AndExternalRollback_DoesNot()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();

        // Commit case
        Guid committedFromId;
        Guid committedToId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var (fromId, toId) = await CreateTwoDraftDocsAsync(scope.ServiceProvider);

            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            committedFromId = fromId;
            committedToId = toId;

            await uow.BeginTransactionAsync();
            (await svc.CreateAsync(fromId, toId, code, manageTransaction: false, ct: CancellationToken.None))
                .Should().BeTrue();
            await uow.CommitAsync();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM document_relationships WHERE from_document_id = @fromDocumentId AND to_document_id = @toDocumentId AND relationship_code_norm = @codeNorm;",
                new { fromDocumentId = committedFromId, toDocumentId = committedToId, codeNorm });
            count.Should().Be(1);
        }

        // Rollback case
        Guid rolledBackFromId;
        Guid rolledBackToId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var (fromId, toId) = await CreateTwoDraftDocsAsync(scope.ServiceProvider);

            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            rolledBackFromId = fromId;
            rolledBackToId = toId;

            await uow.BeginTransactionAsync();
            (await svc.CreateAsync(fromId, toId, code, manageTransaction: false, ct: CancellationToken.None))
                .Should().BeTrue();
            await uow.RollbackAsync();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM document_relationships WHERE from_document_id = @fromDocumentId AND to_document_id = @toDocumentId AND relationship_code_norm = @codeNorm;",
                new { fromDocumentId = rolledBackFromId, toDocumentId = rolledBackToId, codeNorm });
            count.Should().Be(0);
        }
    }

    [Fact]
    public async Task DeleteAsync_ManageTransactionFalse_ExternalCommit_Deletes_AndExternalRollback_DoesNot()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "based_on";
        var codeNorm = code.ToLowerInvariant();

        // Commit delete
        Guid commitFromId;
        Guid commitToId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            (commitFromId, commitToId) = await CreateTwoDraftDocsAsync(scope.ServiceProvider);

            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            (await svc.CreateAsync(commitFromId, commitToId, code, manageTransaction: true, ct: CancellationToken.None))
                .Should().BeTrue();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            (await svc.DeleteAsync(commitFromId, commitToId, code, manageTransaction: false, ct: CancellationToken.None))
                .Should().BeTrue();
            await uow.CommitAsync();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM document_relationships WHERE from_document_id = @fromDocumentId AND to_document_id = @toDocumentId AND relationship_code_norm = @codeNorm;",
                new { fromDocumentId = commitFromId, toDocumentId = commitToId, codeNorm });
            count.Should().Be(0);
        }

        // Rollback delete
        Guid rollbackFromId;
        Guid rollbackToId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            (rollbackFromId, rollbackToId) = await CreateTwoDraftDocsAsync(scope.ServiceProvider);

            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            (await svc.CreateAsync(rollbackFromId, rollbackToId, code, manageTransaction: true, ct: CancellationToken.None))
                .Should().BeTrue();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            (await svc.DeleteAsync(rollbackFromId, rollbackToId, code, manageTransaction: false, ct: CancellationToken.None))
                .Should().BeTrue();
            await uow.RollbackAsync();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM document_relationships WHERE from_document_id = @fromDocumentId AND to_document_id = @toDocumentId AND relationship_code_norm = @codeNorm;",
                new { fromDocumentId = rollbackFromId, toDocumentId = rollbackToId, codeNorm });
            count.Should().Be(1);
        }
    }

    private static async Task<(Guid FromId, Guid ToId)> CreateTwoDraftDocsAsync(IServiceProvider sp)
    {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = $"IT-A-{nowUtc:HHmmssfff}",
                DateUtc = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await repo.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it_beta",
                Number = $"IT-B-{nowUtc:HHmmssfff}",
                DateUtc = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return (fromId, toId);
    }
}
