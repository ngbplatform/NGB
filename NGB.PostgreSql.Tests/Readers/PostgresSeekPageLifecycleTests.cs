using System.Data;
using System.Data.Common;
using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Metadata.Base;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Documents.Universal;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Tests.TestDoubles;
using Xunit;

namespace NGB.PostgreSql.Tests.Readers;

public sealed class PostgresSeekPageLifecycleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Filtered_page_returns_total_and_materialized_rows_after_disposing_both_result_sets(bool document, bool hasRows)
    {
        var id = Guid.NewGuid();
        var count = new DataTable();
        count.Columns.Add("Total", typeof(long));
        count.Rows.Add(hasRows ? 1L : 0L);
        var rows = new DataTable();
        rows.Columns.Add("Id", typeof(Guid));
        rows.Columns.Add("Display", typeof(string));
        rows.Columns.Add("SortDisplay", typeof(string));
        rows.Columns.Add("name", typeof(string));
        if (document)
        {
            rows.Columns.Add("Status", typeof(short));
            rows.Columns.Add("Number", typeof(string));
            if (hasRows) rows.Rows.Add(id, "Alpha", "Alpha", "Alpha", (short)DocumentStatus.Draft, "INV-1");
        }
        else
        {
            rows.Columns.Add("IsDeleted", typeof(bool));
            if (hasRows) rows.Rows.Add(id, "Alpha", "Alpha", "Alpha", false);
        }
        using var results = new DataTableReader([count, rows]);
        var connection = new RecordingDbConnection(readerFactory: _ => results);
        var uow = new RecordingUnitOfWork(connection);

        if (document)
        {
            var page = await new PostgresDocumentReader(uow, []).GetSeekPageAsync(
                new("invoice", "doc_invoice", "name", [new("name", ColumnType.String)]),
                new("Alpha", []), null, null, 10, includeTotal: true);
            page.Total.Should().Be(hasRows ? 1 : 0);
            page.Rows.Should().HaveCount(hasRows ? 1 : 0);
            page.HasMore.Should().BeFalse();
            if (hasRows)
            {
                page.Rows[0].Id.Should().Be(id);
                page.Rows[0].Display.Should().Be("Alpha");
                page.Rows[0].Fields["name"].Should().Be("Alpha");
                page.Rows[0].Status.Should().Be(DocumentStatus.Draft);
                page.Rows[0].Number.Should().Be("INV-1");
            }
        }
        else
        {
            var page = await new PostgresCatalogReader(uow).GetSeekPageAsync(
                new("customers", "cat_customers", "name", [new("name", ColumnType.String)]),
                new("Alpha", []), null, null, 10, includeTotal: true);
            page.Total.Should().Be(hasRows ? 1 : 0);
            page.Rows.Should().HaveCount(hasRows ? 1 : 0);
            page.HasMore.Should().BeFalse();
            if (hasRows)
            {
                page.Rows[0].Id.Should().Be(id);
                page.Rows[0].Display.Should().Be("Alpha");
                page.Rows[0].Fields["name"].Should().Be("Alpha");
                page.Rows[0].IsMarkedForDeletion.Should().BeFalse();
            }
        }

        results.IsClosed.Should().BeTrue();
        connection.Commands.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, true)]
    public async Task Failed_count_or_page_read_awaits_cleanup_and_propagates_read_or_disposal_error(
        bool document, bool failPage, bool asynchronousDisposal, bool disposalFails)
    {
        var original = new InvalidOperationException("Injected result read failure");
        var disposalError = new InvalidOperationException("Injected reader disposal failure");
        var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Mock<DbDataReader>();
        var resultIndex = 0;
        var reads = 0;
        int? failedResult = null;
        reader.SetupGet(x => x.FieldCount).Returns(1);
        reader.Setup(x => x.GetName(0)).Returns("Total");
        reader.Setup(x => x.GetFieldType(0)).Returns(typeof(long));
        reader.Setup(x => x.GetValue(0)).Returns(5L);
        reader.Setup(x => x.ReadAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (!failPage || resultIndex == 1)
            {
                failedResult = resultIndex;
                return Task.FromException<bool>(original);
            }
            return Task.FromResult(++reads == 1);
        });
        reader.Setup(x => x.NextResultAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            // A failed page cannot be drained to advance to another result set either.
            if (resultIndex == 1) return Task.FromException<bool>(original);
            resultIndex++;
            return Task.FromResult(true);
        });
        reader.Setup(x => x.DisposeAsync()).Returns(() =>
        {
            disposeStarted.TrySetResult();
            if (asynchronousDisposal) return new ValueTask(releaseDispose.Task);
            return disposalFails ? ValueTask.FromException(disposalError) : ValueTask.CompletedTask;
        });
        var connection = new RecordingDbConnection(readerFactory: _ => reader.Object);
        var uow = new RecordingUnitOfWork(connection);

        Task operation = document
            ? new PostgresDocumentReader(uow, []).GetSeekPageAsync(
                new DocumentHeadDescriptor("invoice", "doc_invoice", "name", [new("name", ColumnType.String)]),
                new DocumentQuery(null, []), null, null, 10, includeTotal: true)
            : new PostgresCatalogReader(uow).GetSeekPageAsync(
                new CatalogHeadDescriptor("customers", "cat_customers", "name", [new("name", ColumnType.String)]),
                new CatalogQuery(null, []), null, null, 10, includeTotal: true);
        try
        {
            await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (asynchronousDisposal)
                operation.IsCompleted.Should().BeFalse("failure propagation must wait for reader cleanup");
        }
        finally
        {
            if (asynchronousDisposal && disposalFails) releaseDispose.TrySetException(disposalError);
            else releaseDispose.TrySetResult();
        }

        var error = await ((Func<Task>)(() => operation)).Should().ThrowAsync<InvalidOperationException>();
        error.Which.Should().BeSameAs(disposalFails ? disposalError : original);
        reader.Verify(x => x.DisposeAsync(), Times.Once);
        connection.Commands.Should().ContainSingle("a failed count/page must not trigger a fallback query");
        failedResult.Should().Be(failPage ? 1 : 0);
    }
}
