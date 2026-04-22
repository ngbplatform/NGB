using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Numbering;

/// <summary>
/// P2: Low-level uniqueness/atomicity contract for documents.number.
/// We intentionally test the repository directly to cover cases where higher-level services are bypassed.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentRepository_TrySetNumber_ConcurrencyAndUniqueness_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TrySetNumberAsync_SameDocument_Twice_SecondCallIsNoOp_ReturnsFalse_AndDoesNotChangeUpdatedAt()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var docId = Guid.CreateVersion7();
        await CreateDraftDocAsync(host, docId, typeCode: "IT", dateUtc: T0);

        var number = "TEST-0001";
        var t1 = T0.AddMinutes(1);
        var t2 = T0.AddMinutes(2);

        // First assignment sets the number.
        var set1 = await TrySetNumberInTxnAsync(host, docId, number, t1);
        set1.Should().BeTrue();

        // Second assignment is a no-op (number is already set).
        var set2 = await TrySetNumberInTxnAsync(host, docId, number: "TEST-9999", updatedAtUtc: t2);
        set2.Should().BeFalse();

        // And updated_at_utc must remain unchanged by the no-op.
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var doc = await repo.GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Number.Should().Be(number);
        doc.UpdatedAtUtc.Should().Be(t1);
    }

    [Fact]
    public async Task TrySetNumberAsync_TwoDocuments_CompeteForSameNumber_ExactlyOneWins_OtherGetsUniqueViolation()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        await CreateDraftDocAsync(host, doc1, typeCode: "IT", dateUtc: T0);
        await CreateDraftDocAsync(host, doc2, typeCode: "IT", dateUtc: T0);

        var number = "RACE-0001";
        var updatedAt = T0.AddMinutes(5);

        var gate = new Barrier(participantCount: 2);

        var t1 = Task.Run(() => TrySetNumberInOwnTxn_CaptureAsync(host, doc1, number, updatedAt, gate));
        var t2 = Task.Run(() => TrySetNumberInOwnTxn_CaptureAsync(host, doc2, number, updatedAt, gate));

        var results = await Task.WhenAll(t1, t2);

        results.Count(r => r == TrySetOutcome.Set).Should().Be(1);
        results.Count(r => r == TrySetOutcome.UniqueViolation).Should().Be(1);

        // DB invariant: only one document can hold this number.
        var assignedCount = await CountDocumentsWithNumberAsync(Fixture.ConnectionString, typeCode: "IT", number);
        assignedCount.Should().Be(1);
    }

    private enum TrySetOutcome
    {
        Set,
        UniqueViolation,
        NoOp
    }

    private static async Task CreateDraftDocAsync(IHost host, Guid id, string typeCode, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.CreateAsync(new DocumentRecord
        {
            Id = id,
            TypeCode = typeCode,
            Number = null,
            DateUtc = dateUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = dateUtc,
            UpdatedAtUtc = dateUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task<bool> TrySetNumberInTxnAsync(IHost host, Guid documentId, string number, DateTime updatedAtUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        var set = await repo.TrySetNumberAsync(documentId, number, updatedAtUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
        return set;
    }

    private static async Task<TrySetOutcome> TrySetNumberInOwnTxn_CaptureAsync(
        IHost host,
        Guid documentId,
        string number,
        DateTime updatedAtUtc,
        Barrier gate)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        // Try to start both attempts at roughly the same time.
        gate.SignalAndWait(TimeSpan.FromSeconds(10));

        try
        {
            var set = await repo.TrySetNumberAsync(documentId, number, updatedAtUtc, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return set ? TrySetOutcome.Set : TrySetOutcome.NoOp;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await uow.RollbackAsync(CancellationToken.None);
            return TrySetOutcome.UniqueViolation;
        }
    }

    private static async Task<int> CountDocumentsWithNumberAsync(string cs, string typeCode, string number)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM documents WHERE type_code = @type_code AND number = @number;",
            conn);
        cmd.Parameters.AddWithValue("type_code", typeCode);
        cmd.Parameters.AddWithValue("number", number);

        var result = await cmd.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt32(result);
    }
}
