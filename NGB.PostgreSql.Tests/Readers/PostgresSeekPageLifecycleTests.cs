using System.Data;
using System.Data.Common;
using FluentAssertions;
using Moq;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_filtered_page_returns_total_and_disposes_both_result_sets(bool document)
    {
        var count = new DataTable();
        count.Columns.Add("Total", typeof(long));
        count.Rows.Add(0L);
        var rows = new DataTable();
        rows.Columns.Add("Id", typeof(Guid));
        using var results = new DataTableReader([count, rows]);
        var connection = new RecordingDbConnection(readerFactory: _ => results);
        var uow = new RecordingUnitOfWork(connection);

        if (document)
        {
            var page = await new PostgresDocumentReader(uow, []).GetSeekPageAsync(
                new("invoice", "doc_invoice", "name", [new("name", ColumnType.String)]),
                new("Alpha", []), null, null, 10, includeTotal: true);
            page.Total.Should().Be(0);
            page.Rows.Should().BeEmpty();
            page.HasMore.Should().BeFalse();
        }
        else
        {
            var page = await new PostgresCatalogReader(uow).GetSeekPageAsync(
                new("customers", "cat_customers", "name", [new("name", ColumnType.String)]),
                new("Alpha", []), null, null, 10, includeTotal: true);
            page.Total.Should().Be(0);
            page.Rows.Should().BeEmpty();
            page.HasMore.Should().BeFalse();
        }

        results.IsClosed.Should().BeTrue();
        connection.Commands.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Failed_count_or_page_read_disposes_results_before_propagating_original_error(
        bool document, bool failPage, bool asynchronousDisposal)
    {
        var original = new InvalidOperationException("Injected result read failure");
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
            return asynchronousDisposal ? new ValueTask(releaseDispose.Task) : ValueTask.CompletedTask;
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
            releaseDispose.TrySetResult();
        }

        var error = await ((Func<Task>)(() => operation)).Should().ThrowAsync<InvalidOperationException>();
        error.Which.Should().BeSameAs(original);
        reader.Verify(x => x.DisposeAsync(), Times.Once);
        connection.Commands.Should().ContainSingle("a failed count/page must not trigger a fallback query");
        failedResult.Should().Be(failPage ? 1 : 0);
    }
}
