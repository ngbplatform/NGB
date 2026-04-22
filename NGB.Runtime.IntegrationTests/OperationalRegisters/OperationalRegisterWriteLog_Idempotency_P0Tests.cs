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
/// P0: Idempotency semantics for operational_register_write_state.
/// Mirrors accounting posting_log behaviors (Begun / InProgress / AlreadyCompleted + stale takeover).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterWriteLog_Idempotency_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TryBegin_IsIdempotent_And_MarkCompleted_MakesFutureBeginsAlreadyCompleted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var log = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteStateRepository>();

        var regId = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await SeedRegisterAndDocumentAsync(regRepo, docs, regId, docId, nowUtc);

        var r1 = await log.TryBeginAsync(regId, docId, OperationalRegisterWriteOperation.Post, nowUtc, CancellationToken.None);
        r1.Should().Be(PostingStateBeginResult.Begun);

        var r2 = await log.TryBeginAsync(regId, docId, OperationalRegisterWriteOperation.Post, nowUtc, CancellationToken.None);
        r2.Should().Be(PostingStateBeginResult.InProgress);

        var completedAtUtc = nowUtc.AddSeconds(1);
        await log.MarkCompletedAsync(regId, docId, OperationalRegisterWriteOperation.Post, completedAtUtc, CancellationToken.None);

        var r3 = await log.TryBeginAsync(regId, docId, OperationalRegisterWriteOperation.Post, nowUtc.AddSeconds(2), CancellationToken.None);
        r3.Should().Be(PostingStateBeginResult.AlreadyCompleted);

        await uow.CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TryBegin_WhenExistingInProgressIsStale_AllowsSafeTakeover()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var log = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteStateRepository>();

        var regId = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var staleStartedAtUtc = nowUtc.AddMinutes(-11);

        // Txn#1: seed and start the log with a stale started_at_utc (simulate crash).
        await uow.BeginTransactionAsync(CancellationToken.None);
        await SeedRegisterAndDocumentAsync(regRepo, docs, regId, docId, nowUtc);

        var begun = await log.TryBeginAsync(regId, docId, OperationalRegisterWriteOperation.Post, staleStartedAtUtc, CancellationToken.None);
        begun.Should().Be(PostingStateBeginResult.Begun);

        await uow.CommitAsync(CancellationToken.None);

        // Txn#2: a new attempt should take over (update started_at_utc) and return Begun.
        await uow.BeginTransactionAsync(CancellationToken.None);

        var takeOver = await log.TryBeginAsync(regId, docId, OperationalRegisterWriteOperation.Post, nowUtc, CancellationToken.None);
        takeOver.Should().Be(PostingStateBeginResult.Begun);

        var row = await uow.Connection.QuerySingleAsync<(DateTime StartedAtUtc, DateTime? CompletedAtUtc)>(
            new CommandDefinition(
                "SELECT started_at_utc AS StartedAtUtc, completed_at_utc AS CompletedAtUtc FROM operational_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O;",
                new { R = regId, D = docId, O = (short)OperationalRegisterWriteOperation.Post },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

        row.CompletedAtUtc.Should().BeNull();
        row.StartedAtUtc.Should().BeCloseTo(nowUtc, precision: TimeSpan.FromSeconds(5));

        await uow.CommitAsync(CancellationToken.None);
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
            DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);
    }
}
