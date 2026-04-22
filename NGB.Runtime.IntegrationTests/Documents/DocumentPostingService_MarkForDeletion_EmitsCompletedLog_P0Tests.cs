using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Accounting.Posting.Validators;
using NGB.Definitions;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

/// <summary>
/// P0: MarkForDeletion must emit a Completed lifecycle log record (not a NoOp).
/// Logging is used by ops / diagnostics and should remain consistent.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_MarkForDeletion_EmitsCompletedLog_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MarkForDeletionAsync_OnDraft_EmitsCompletedLog_NotNoOp()
    {
        var sink = new TestLogSink();
        using var host = CreateHost(Fixture.ConnectionString, sink);

        var docId = await CreateDraftAsync(host, new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc), "INV-LOG-1");

        await MarkForDeletionAsync(host, docId);

        sink.Records.Should().ContainSingle(r =>
            r.EventId.Id == 1001 && r.Message.Contains("MarkForDeletion", StringComparison.Ordinal));

        sink.Records.Should().NotContain(r =>
            r.EventId.Id == 1002 && r.Message.Contains("MarkForDeletion", StringComparison.Ordinal));
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

                // PostingEngine requires the accounting validator even if this test focuses on documents.
                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
            })
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = true;
            })
            .Build();
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, DateTime dateUtc, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: "demo.sales_invoice",
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task MarkForDeletionAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await posting.MarkForDeletionAsync(documentId, CancellationToken.None);
    }
}
