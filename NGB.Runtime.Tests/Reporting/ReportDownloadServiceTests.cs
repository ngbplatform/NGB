using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Persistence.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Streaming;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportDownloadServiceTests
{
    [Fact]
    public async Task Failure_to_begin_read_session_does_not_open_source()
    {
        var source = new Source(1);
        var session = Session();
        session.Setup(x => x.BeginAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("begin failed"));
        await ((Func<Task>)(async () => await Create(source, session.Object).PrepareAsync("test", new(), default)))
            .Should().ThrowAsync<IOException>();
        source.ReadCount.Should().Be(0);
        session.Verify(x => x.EndAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Out_of_order_rows_fail_export_and_missing_metadata_has_no_title()
    {
        var source = new Source(2) { OrdinalOffset = 1, WithoutMetadata = true };
        var session = Session();
        var download = await Create(source, session.Object).PrepareAsync("test", new(), default);
        download.Title.Should().BeNull();
        using var output = new MemoryStream();
        await ((Func<Task>)(() => download.WriteAsync(output, default)))
            .Should().ThrowAsync<NGB.Tools.Exceptions.NgbInvariantViolationException>().WithMessage("*exactly once*");
        await download.DisposeAsync();
        await download.DisposeAsync();
        await ((Func<Task>)(() => download.WriteAsync(output, default))).Should().ThrowAsync<ObjectDisposedException>();
        session.Verify(x => x.EndAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_session_cleanup_still_disposes_source(bool duringPreparation)
    {
        var source = new Source(1) { FailAt = duringPreparation ? 0 : null };
        var session = Session();
        session.Setup(x => x.EndAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("rollback failed"));
        if (duringPreparation)
            await ((Func<Task>)(async () => await Create(source, session.Object).PrepareAsync("test", new(), default))).Should().ThrowAsync<IOException>();
        else
        {
            var download = await Create(source, session.Object).PrepareAsync("test", new(), default);
            await ((Func<Task>)(async () => await download.DisposeAsync())).Should().ThrowAsync<IOException>();
        }
        source.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Preparation_reads_only_first_row_and_disposal_releases_source_and_transaction()
    {
        var source = new Source(100_000);
        var session = Session();
        var service = Create(source, session.Object);
        await using (var download = await service.PrepareAsync("test", new(), default))
        {
            source.ReadCount.Should().Be(1);
            source.Disposed.Should().BeFalse();
            download.Title.Should().Be("Test");
        }
        source.Disposed.Should().BeTrue();
        session.Verify(x => x.BeginAsync(It.IsAny<CancellationToken>()), Times.Once);
        session.Verify(x => x.EndAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Source_cleanup_failure_still_ends_the_read_session(bool duringPreparation)
    {
        var source = new Source(1) { FailAt = duringPreparation ? 0 : null, FailDispose = true };
        var session = Session();
        if (duringPreparation)
            await ((Func<Task>)(async () => await Create(source, session.Object).PrepareAsync("test", new(), default)))
                .Should().ThrowAsync<IOException>().WithMessage("source cleanup failed");
        else
        {
            var download = await Create(source, session.Object).PrepareAsync("test", new(), default);
            await ((Func<Task>)(async () => await download.DisposeAsync()))
                .Should().ThrowAsync<IOException>().WithMessage("source cleanup failed");
        }
        session.Verify(x => x.EndAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Source_error_during_preparation_releases_the_read_session()
    {
        var source = new Source(10) { FailAt = 0 };
        var session = Session();
        await ((Func<Task>)(async () => await Create(source, session.Object).PrepareAsync("test", new(), default)))
            .Should().ThrowAsync<InvalidOperationException>();
        source.Disposed.Should().BeTrue();
        session.Verify(x => x.EndAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failure_to_read_and_dispose_the_first_row_still_ends_the_session()
    {
        var source = new Mock<IStreamingReportExecutor>();
        source.SetupGet(x => x.ReportCode).Returns("test");
        source.Setup(x => x.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(new FailingRows());
        var session = Session();
        await ((Func<Task>)(async () => await Create(source.Object, session.Object).PrepareAsync("test", new(), default)))
            .Should().ThrowAsync<IOException>().WithMessage("dispose failed");
        session.Verify(x => x.EndAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Export_is_single_use_and_source_failure_is_not_reported_as_success()
    {
        var source = new Source(100) { FailAt = 3 };
        var session = Session();
        await using (var download = await Create(source, session.Object).PrepareAsync("test", new(), default))
        {
            using var output = new MemoryStream();
            await ((Func<Task>)(() => download.WriteAsync(output, default))).Should().ThrowAsync<InvalidOperationException>();
            await ((Func<Task>)(() => download.WriteAsync(output, default))).Should().ThrowAsync<InvalidOperationException>();
            source.ReadCount.Should().Be(3);
        }
        session.Verify(x => x.EndAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancellation_of_the_write_token_stops_source_reading_and_releases_the_snapshot()
    {
        var source = new Source(100_000);
        var session = Session();
        using var cancellation = new CancellationTokenSource();
        await using (var download = await Create(source, session.Object).PrepareAsync("test", new(), default))
        {
            cancellation.Cancel();
            using var output = new MemoryStream();
            await ((Func<Task>)(() => download.WriteAsync(output, cancellation.Token)))
                .Should().ThrowAsync<OperationCanceledException>();
            source.ReadCount.Should().Be(1);
        }
        source.Disposed.Should().BeTrue();
        session.Verify(x => x.EndAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<IReportReadSession> Session()
    {
        var session = new Mock<IReportReadSession>();
        session.Setup(x => x.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        session.Setup(x => x.EndAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return session;
    }

    private static ReportDownloadService Create(IStreamingReportExecutor source, IReportReadSession session)
    {
        var definitions = new Mock<IReportDefinitionProvider>();
        definitions.Setup(x => x.GetDefinitionAsync("test", It.IsAny<CancellationToken>())).ReturnsAsync(new ReportDefinitionDto("test", "Test"));
        return new([source], definitions.Object, Mock.Of<IReportLayoutValidator>(),
            new(Mock.Of<IReportVariantService>()), new(), session, new ReportXlsxExportService());
    }

    private sealed class FailingRows : IAsyncEnumerable<ReportRowWrite>, IAsyncEnumerator<ReportRowWrite>
    {
        public ReportRowWrite Current => throw new InvalidOperationException("There is no current row.");
        public IAsyncEnumerator<ReportRowWrite> GetAsyncEnumerator(CancellationToken ct = default) => this;
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromException<bool>(new InvalidOperationException("read failed"));
        public ValueTask DisposeAsync() => ValueTask.FromException(new IOException("dispose failed"));
    }

    private sealed class Source(int count) : IStreamingReportExecutor
    {
        public string ReportCode => "test";
        public int ReadCount { get; private set; }
        public int? FailAt { get; init; }
        public int OrdinalOffset { get; init; }
        public bool WithoutMetadata { get; init; }
        public bool FailDispose { get; init; }
        public bool Disposed { get; private set; }
        public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request) => "";
        public ReportSheetDto Template(string preparedJson) => new([new("n", "Number", "int32")], [], WithoutMetadata ? null : new(Title: "Test"));
        public async IAsyncEnumerable<ReportRowWrite> ReadAsync(string preparedJson, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (FailAt == i) throw new InvalidOperationException("Source failed.");
                    ReadCount++;
                    yield return new(i + OrdinalOffset, new(ReportRowKind.Detail, [new(JsonSerializer.SerializeToElement(i), ValueType: "int32")]));
                }
            }
            finally
            {
                Disposed = true;
                if (FailDispose) throw new IOException("source cleanup failed");
            }
        }
    }
}
