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

    private static ReportDownloadService Create(Source source, IReportReadSession session)
    {
        var definitions = new Mock<IReportDefinitionProvider>();
        definitions.Setup(x => x.GetDefinitionAsync("test", It.IsAny<CancellationToken>())).ReturnsAsync(new ReportDefinitionDto("test", "Test"));
        return new([source], definitions.Object, Mock.Of<IReportLayoutValidator>(),
            new(Mock.Of<IReportVariantService>()), new(), session, new ReportXlsxExportService());
    }

    private sealed class Source(int count) : IStreamingReportExecutor
    {
        public string ReportCode => "test";
        public int ReadCount { get; private set; }
        public int? FailAt { get; init; }
        public bool Disposed { get; private set; }
        public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request) => "";
        public ReportSheetDto Template(string preparedJson) => new([new("n", "Number", "int32")], [], new(Title: "Test"));
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
                    yield return new(i, new(ReportRowKind.Detail, [new(JsonSerializer.SerializeToElement(i), ValueType: "int32")]));
                }
            }
            finally { Disposed = true; }
        }
    }
}
