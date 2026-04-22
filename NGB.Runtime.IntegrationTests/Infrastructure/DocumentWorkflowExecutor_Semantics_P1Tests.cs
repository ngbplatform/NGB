using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Accounting.Posting.Validators;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1: Canonical workflow helper semantics.
/// - No Completed log when action returns no-op.
/// - Exceptions rollback the transaction (no partial writes).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentWorkflowExecutor_Semantics_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ExecuteAsync_WhenActionReturnsNoOp_DoesNotLogCompleted()
    {
        var sink = new TestLogSink();
        var host = CreateHost(Fixture.ConnectionString, sink);

        await using var scope = host.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IDocumentWorkflowExecutor>();

        await executor.ExecuteAsync(
            operationName: "Test.NoOp",
            documentId: Guid.CreateVersion7(),
            action: _ => Task.FromResult(false),
            manageTransaction: true,
            ct: default);

        sink.Records.Should().ContainSingle(r => r.EventId.Id == 1000);
        sink.Records.Should().ContainSingle(r => r.EventId.Id == 1002);
        sink.Records.Should().NotContain(r => r.EventId.Id == 1001);
    }

    [Fact]
    public async Task ExecuteAsync_WhenActionThrows_RollsBack_NoPartialWrites()
    {
        var sink = new TestLogSink();
        var host = CreateHost(Fixture.ConnectionString, sink);

        const string number = "WF-ROLLBACK-001";

        await using var scope = host.Services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IDocumentWorkflowExecutor>();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var act = () => executor.ExecuteAsync(
            operationName: "Test.Rollback",
            documentId: null,
            action: async innerCt =>
            {
                await drafts.CreateDraftAsync(
                    typeCode: "general_journal_entry",
                    number: number,
                    dateUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    manageTransaction: false,
                    ct: innerCt);

                throw new NotSupportedException("boom");
            },
            manageTransaction: true,
            ct: default);

        var ex = await act.Should().ThrowAsync<NgbUnexpectedException>();
        ex.Which.Message.Should().Be("Unexpected internal error.");
        ex.Which.InnerException.Should().BeOfType<NotSupportedException>().Which.Message.Should().Be("boom");

        // Assert rollback: draft must not exist.
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select count(*) from documents where number = @n", conn);
        cmd.Parameters.AddWithValue("n", number);

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.Should().Be(0);

        // Lifecycle logs: Started is always logged; Completed/NoOp are not logged on exception.
        sink.Records.Should().ContainSingle(r => r.EventId.Id == 1000);
        sink.Records.Should().NotContain(r => r.EventId.Id == 1001);
        sink.Records.Should().NotContain(r => r.EventId.Id == 1002);
    }

    private static IHost CreateHost(string connectionString, TestLogSink sink)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(sink);
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices(services =>
            {
                services.AddNgbRuntime();
                services.AddNgbPostgres(connectionString);
                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();
            })
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = true;
            })
            .Build();
    }
}
