using System.Data.Common;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Reporting.Runs;
using Dapper;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class ReportRunRecovery_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Transient_failure_restarts_the_stream_and_publishes_only_the_successful_attempt()
    {
        var executor = new ControlledExecutor { FailOnce = true };
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services => services.AddSingleton<IStreamingReportExecutor>(executor));
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IReportRunStore>();
        var processor = host.Services.GetRequiredService<ReportRunProcessor>();
        var id = Guid.NewGuid();
        await store.CreateAsync(id, "owner", executor.ReportCode, "{}", default);
        await processor.ProcessNextAsync(default);
        var failedAttempt = (await store.FindAsync(id, "owner", executor.ReportCode, default))!;
        failedAttempt.Status.Should().Be("Running");
        (await store.ReadRowsAsync(id, failedAttempt.Attempt, 0, 500, default)).Should().HaveCount(256);
        var service = scope.ServiceProvider.GetRequiredService<IReportRunService>();
        await ((Func<Task>)(() => service.ReadAsync(executor.ReportCode, "owner", id, 0, 500, default))).Should().ThrowAsync<ReportRunNotReadyException>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.Connection.ExecuteAsync("UPDATE platform_report_runs SET lease_until_utc=now()-interval '1 minute' WHERE id=@id", new { id });
        await processor.ProcessNextAsync(default);
        var completed = (await store.FindAsync(id, "owner", executor.ReportCode, default))!;
        completed.Status.Should().Be("Ready");
        completed.Attempt.Should().NotBe(failedAttempt.Attempt);
        completed.RowCount.Should().Be(300);
        (await service.ReadAsync(executor.ReportCode, "owner", id, 0, 500, default)).Sheet.Rows.Should().HaveCount(300);
    }

    [Fact]
    public async Task Cancelling_active_work_cancels_the_source_and_never_publishes_partial_rows()
    {
        var executor = new ControlledExecutor { Block = true };
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services => services.AddSingleton<IStreamingReportExecutor>(executor));
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IReportRunStore>();
        var id = Guid.NewGuid();
        await store.CreateAsync(id, "owner", executor.ReportCode, "{}", default);
        var work = host.Services.GetRequiredService<ReportRunProcessor>().ProcessNextAsync(default);
        await executor.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await store.CancelAsync(id, "owner", executor.ReportCode, default);
        await work.WaitAsync(TimeSpan.FromSeconds(10));
        executor.Cancelled.Should().BeTrue();
        (await store.FindAsync(id, "owner", executor.ReportCode, default))!.Status.Should().Be("Cancelled");
        await store.CleanupAsync(default);
    }

    private sealed class ControlledExecutor : IStreamingReportExecutor
    {
        public string ReportCode => "it.streaming-report";
        public bool FailOnce { get; set; }
        public bool Block { get; init; }
        public bool Cancelled { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Prepare(ReportDefinitionDto definition, ReportExecutionRequestDto request) => "{}";
        public ReportSheetDto Template(string preparedJson) => new([new("text", "Text", "string")], []);
        public async IAsyncEnumerable<ReportRowWrite> ReadAsync(string preparedJson, [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 0; i < 300; i++)
            {
                if (i == 256)
                {
                    if (FailOnce) { FailOnce = false; throw new TransientFailure(); }
                    if (Block)
                    {
                        Started.TrySetResult();
                        try { await Task.Delay(Timeout.Infinite, ct); }
                        catch (OperationCanceledException) { Cancelled = true; throw; }
                    }
                }
                yield return new(i, new(ReportRowKind.Detail, [new(Display: i.ToString(), ValueType: "string")]));
            }
        }
    }

    private sealed class TransientFailure : DbException
    {
        public override bool IsTransient => true;
    }
}
