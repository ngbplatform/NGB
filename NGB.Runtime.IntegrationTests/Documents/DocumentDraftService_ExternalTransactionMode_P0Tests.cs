using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

/// <summary>
/// P0: IDocumentDraftService must fully support external transaction mode:
/// - manageTransaction=false without an ambient transaction => fail fast with canonical message,
/// - manageTransaction=false with an ambient transaction => use it and must not commit/rollback implicitly.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);
    private const string KnownTypeCode = "general_journal_entry";

    [Fact]
    public async Task CreateDraft_WhenManageTransactionFalse_AndNoAmbientTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var act = () => drafts.CreateDraftAsync(
            typeCode: KnownTypeCode,
            number: "GJE-EXT-1",
            dateUtc: NowUtc,
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task UpdateDraft_WhenManageTransactionFalse_AndNoAmbientTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            docId = await drafts.CreateDraftAsync(KnownTypeCode, "GJE-UPD-1", NowUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            var act = () => drafts.UpdateDraftAsync(
                documentId: docId,
                number: "GJE-UPD-2",
                dateUtc: null,
                manageTransaction: false,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("This operation requires an active transaction.");
        }
    }

    [Fact]
    public async Task DeleteDraft_WhenManageTransactionFalse_AndNoAmbientTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            docId = await drafts.CreateDraftAsync(KnownTypeCode, "GJE-DEL-1", NowUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            var act = () => drafts.DeleteDraftAsync(
                documentId: docId,
                manageTransaction: false,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("This operation requires an active transaction.");
        }
    }

    [Fact]
    public async Task UpdateDraft_WhenManageTransactionFalse_UsesAmbientTransaction_AndDoesNotCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            docId = await drafts.CreateDraftAsync(KnownTypeCode, "GJE-TXN-1", NowUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                var changed = await drafts.UpdateDraftAsync(
                    documentId: docId,
                    number: "GJE-TXN-2",
                    dateUtc: null,
                    manageTransaction: false,
                    ct: CancellationToken.None);

                changed.Should().BeTrue();
                uow.HasActiveTransaction.Should().BeTrue("external transaction mode must not auto-commit");

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                try { await uow.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                throw;
            }
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await repo.GetAsync(docId, CancellationToken.None);

            doc.Should().NotBeNull();
            doc!.TypeCode.Should().Be(KnownTypeCode);
            doc.Number.Should().Be("GJE-TXN-2");
        }
    }

    [Fact]
    public async Task DeleteDraft_WhenManageTransactionFalse_UsesAmbientTransaction_AndDoesNotCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid docId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            docId = await drafts.CreateDraftAsync(KnownTypeCode, "GJE-TXN-DEL-1", NowUtc, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                var deleted = await drafts.DeleteDraftAsync(
                    documentId: docId,
                    manageTransaction: false,
                    ct: CancellationToken.None);

                deleted.Should().BeTrue();
                uow.HasActiveTransaction.Should().BeTrue("external transaction mode must not auto-commit");

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                try { await uow.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                throw;
            }
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await repo.GetAsync(docId, CancellationToken.None);
            doc.Should().BeNull();
        }
    }
}
