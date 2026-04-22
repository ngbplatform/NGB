using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// P1: PostingLog filters correctness (UI relies on this for drill-down).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingLogReader_Filters_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Filters_ByDocumentId_ByOperation_ByStatus_And_ByDateRange_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var now = DateTime.UtcNow;
        var docA = Guid.CreateVersion7();
        var docB = Guid.CreateVersion7();
        var docC = Guid.CreateVersion7();

        // Seed:
        // - docA: Post (completed) + Unpost (completed)
        // - docB: Post (in-progress)
        // - docC: Post (stale)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync(CancellationToken.None);

            await uow.Connection.ExecuteAsync(
                "INSERT INTO accounting_posting_state(document_id, operation, started_at_utc, completed_at_utc) VALUES (@Doc, @Op, @Started, @Completed)",
                new
                {
                    Doc = docA,
                    Op = (short)PostingOperation.Post,
                    Started = now.AddMinutes(-2),
                    Completed = now.AddMinutes(-1)
                },
                uow.Transaction);

            await uow.Connection.ExecuteAsync(
                "INSERT INTO accounting_posting_state(document_id, operation, started_at_utc, completed_at_utc) VALUES (@Doc, @Op, @Started, @Completed)",
                new
                {
                    Doc = docA,
                    Op = (short)PostingOperation.Unpost,
                    Started = now.AddMinutes(-1),
                    Completed = now
                },
                uow.Transaction);

            await uow.Connection.ExecuteAsync(
                "INSERT INTO accounting_posting_state(document_id, operation, started_at_utc, completed_at_utc) VALUES (@Doc, @Op, @Started, NULL)",
                new
                {
                    Doc = docB,
                    Op = (short)PostingOperation.Post,
                    Started = now.AddMinutes(-1)
                },
                uow.Transaction);

            await uow.Connection.ExecuteAsync(
                "INSERT INTO accounting_posting_state(document_id, operation, started_at_utc, completed_at_utc) VALUES (@Doc, @Op, @Started, NULL)",
                new
                {
                    Doc = docC,
                    Op = (short)PostingOperation.Post,
                    Started = now.AddMinutes(-30)
                },
                uow.Transaction);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IPostingStateReader>();

            var baseRequest = new PostingStatePageRequest
            {
                PageSize = 100,
                FromUtc = DateTime.SpecifyKind(now.AddHours(-1), DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(now.AddHours(1), DateTimeKind.Utc),
                StaleAfter = TimeSpan.FromMinutes(10)
            };

            // DocumentId filter
            var byDoc = await reader.GetPageAsync(CreateRequest(baseRequest, documentId: docA), CancellationToken.None);
            byDoc.Records.Should().HaveCount(2);
            byDoc.Records.All(r => r.DocumentId == docA).Should().BeTrue();

            // Operation filter
            var byOp = await reader.GetPageAsync(CreateRequest(baseRequest, operation: PostingOperation.Unpost), CancellationToken.None);
            byOp.Records.Should().HaveCount(1);
            byOp.Records[0].DocumentId.Should().Be(docA);
            byOp.Records[0].Operation.Should().Be(PostingOperation.Unpost);

            // Status filter: Completed
            var completed = await reader.GetPageAsync(CreateRequest(baseRequest, status: PostingStateStatus.Completed), CancellationToken.None);
            completed.Records.Should().HaveCount(2);
            completed.Records.All(r => r.Status == PostingStateStatus.Completed).Should().BeTrue();
            completed.Records.All(r => r.CompletedAtUtc is not null).Should().BeTrue();

            // Status filter: InProgress
            var inProgress = await reader.GetPageAsync(CreateRequest(baseRequest, status: PostingStateStatus.InProgress), CancellationToken.None);
            inProgress.Records.Should().HaveCount(1);
            inProgress.Records[0].DocumentId.Should().Be(docB);
            inProgress.Records[0].Status.Should().Be(PostingStateStatus.InProgress);
            inProgress.Records[0].CompletedAtUtc.Should().BeNull();

            // Status filter: Stale
            var stale = await reader.GetPageAsync(CreateRequest(baseRequest, status: PostingStateStatus.StaleInProgress), CancellationToken.None);
            stale.Records.Should().HaveCount(1);
            stale.Records[0].DocumentId.Should().Be(docC);
            stale.Records[0].Status.Should().Be(PostingStateStatus.StaleInProgress);
            stale.Records[0].CompletedAtUtc.Should().BeNull();

            // Date range filter excludes stale row (started 30min ago)
            var narrowPage = await reader.GetPageAsync(
                CreateRequest(
                    baseRequest,
                    fromUtc: DateTime.SpecifyKind(now.AddMinutes(-5), DateTimeKind.Utc),
                    toUtc: DateTime.SpecifyKind(now.AddMinutes(1), DateTimeKind.Utc),
                    clearFilters: true),
                CancellationToken.None);

            narrowPage.Records.Select(r => r.DocumentId).Should().NotContain(docC);
        }
    }

    private static PostingStatePageRequest CreateRequest(
        PostingStatePageRequest baseRequest,
        Guid? documentId = null,
        PostingOperation? operation = null,
        PostingStateStatus? status = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        bool clearFilters = false)
    {
        return new PostingStatePageRequest
        {
            PageSize = baseRequest.PageSize,
            Cursor = baseRequest.Cursor,
            FromUtc = fromUtc ?? baseRequest.FromUtc,
            ToUtc = toUtc ?? baseRequest.ToUtc,
            StaleAfter = baseRequest.StaleAfter,

            DocumentId = clearFilters ? null : documentId,
            Operation = clearFilters ? null : operation,
            Status = clearFilters ? null : status
        };
    }
}
