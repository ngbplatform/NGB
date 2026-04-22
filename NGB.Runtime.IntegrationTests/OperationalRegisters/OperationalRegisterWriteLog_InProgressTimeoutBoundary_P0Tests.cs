using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Verifies the exact boundary semantics of Operational Registers write log takeover timeout.
///
/// The timeout constant currently lives in PostgresOperationalRegisterWriteStateRepository as:
///   TimeSpan.FromMinutes(10)
/// If you change it, update this test accordingly.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterWriteLog_InProgressTimeoutBoundary_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly TimeSpan InProgressTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task TryBegin_WhenExistingInProgressStartedExactlyAtCutoff_ReturnsInProgress_AndDoesNotTakeOver()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var log = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteStateRepository>();

        var regId = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc);

        var cutoffStartedAtUtc = nowUtc - InProgressTimeout;

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await SeedRegisterAndDocumentAsync(regRepo, docs, regId, docId, nowUtc);

            await InsertWriteLogRowAsync(uow, regId, docId, OperationalRegisterWriteOperation.Post, cutoffStartedAtUtc);

            var r = await log.TryBeginAsync(regId, docId, OperationalRegisterWriteOperation.Post, nowUtc, CancellationToken.None);

            r.Should().Be(
                PostingStateBeginResult.InProgress,
                "takeover must be allowed only when started_at_utc < (startedAtUtc - timeout); equality must not be treated as stale");

            var row = await ReadRowAsync(uow, regId, docId, OperationalRegisterWriteOperation.Post);

            row.CompletedAtUtc.Should().BeNull();
            row.StartedAtUtc.Should().BeCloseTo(cutoffStartedAtUtc, precision: TimeSpan.FromSeconds(1));

            await uow.CommitAsync(CancellationToken.None);
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TryBegin_WhenExistingInProgressStartedBeforeCutoff_AllowsTakeover_AndUpdatesStartedAtUtc()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var log = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteStateRepository>();

        var regId = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc);

        var staleStartedAtUtc = nowUtc - InProgressTimeout - TimeSpan.FromSeconds(1);

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await SeedRegisterAndDocumentAsync(regRepo, docs, regId, docId, nowUtc);

            await InsertWriteLogRowAsync(uow, regId, docId, OperationalRegisterWriteOperation.Post, staleStartedAtUtc);

            var r = await log.TryBeginAsync(regId, docId, OperationalRegisterWriteOperation.Post, nowUtc, CancellationToken.None);

            r.Should().Be(PostingStateBeginResult.Begun);

            var row = await ReadRowAsync(uow, regId, docId, OperationalRegisterWriteOperation.Post);

            row.CompletedAtUtc.Should().BeNull();
            row.StartedAtUtc.Should().BeCloseTo(nowUtc, precision: TimeSpan.FromSeconds(1));

            await uow.CommitAsync(CancellationToken.None);
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task SeedRegisterAndDocumentAsync(
        IOperationalRegisterRepository regRepo,
        IDocumentRepository docs,
        Guid registerId,
        Guid documentId,
        DateTime nowUtc)
    {
        await regRepo.UpsertAsync(new OperationalRegisterUpsert(registerId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

        await docs.CreateAsync(new DocumentRecord
        {
            Id = documentId,
            TypeCode = "it_doc",
            Number = null,
            DateUtc = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);
    }

    private static Task InsertWriteLogRowAsync(
        IUnitOfWork uow,
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation,
        DateTime startedAtUtc)
    {
        const string sql = """
                           INSERT INTO operational_register_write_state(
                               register_id, document_id, operation, started_at_utc, completed_at_utc
                           )
                           VALUES (@R, @D, @O, @S, NULL);
                           """;

        return uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { R = registerId, D = documentId, O = (short)operation, S = startedAtUtc },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
    }

    private static Task<(DateTime StartedAtUtc, DateTime? CompletedAtUtc)> ReadRowAsync(
        IUnitOfWork uow,
        Guid registerId,
        Guid documentId,
        OperationalRegisterWriteOperation operation)
    {
        const string sql = """
                           SELECT started_at_utc AS StartedAtUtc, completed_at_utc AS CompletedAtUtc
                           FROM operational_register_write_state
                           WHERE register_id = @R AND document_id = @D AND operation = @O;
                           """;

        return uow.Connection.QuerySingleAsync<(DateTime StartedAtUtc, DateTime? CompletedAtUtc)>(new CommandDefinition(
            sql,
            new { R = registerId, D = documentId, O = (short)operation },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
    }
}
