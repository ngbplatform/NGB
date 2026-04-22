using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting.Validators;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.Accounts;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

/// <summary>
/// P5: Observability contract for document lifecycle operations.
/// We treat logs as part of the platform API for ops/diagnostics.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_LifecycleLogs_P5Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_OnSuccess_EmitsStartedAndCompleted_NotNoOpOrError()
    {
        var sink = new TestLogSink();
        using var host = CreateHostWithLogSink(Fixture.ConnectionString, sink);
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "LOG-POST-1");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        sink.Records.Should().Contain(r => r.EventId.Id == 1000 && r.Message.Contains("Post", StringComparison.Ordinal));
        sink.Records.Should().Contain(r => r.EventId.Id == 1001 && r.Message.Contains("Post", StringComparison.Ordinal));

        sink.Records.Should().NotContain(r => r.EventId.Id == 1002 && r.Message.Contains("Post", StringComparison.Ordinal));
        sink.Records.Should().NotContain(r => r.Level >= LogLevel.Error && r.Message.Contains("Post failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostAsync_WhenAlreadyPosted_EmitsStartedAndNoOp_NotCompleted()
    {
        var sink = new TestLogSink();
        using var host = CreateHostWithLogSink(Fixture.ConnectionString, sink);
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "LOG-POST-2");

        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        // Act: second post is a strict no-op.
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 999m);

        // We expect at least one no-op for Post, and only a single Completed for Post.
        sink.Records.Should().Contain(r => r.EventId.Id == 1002 && r.Message.Contains("Post", StringComparison.Ordinal));

        sink.Records.Count(r => r.EventId.Id == 1001 && r.Message.Contains("Post", StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Fact]
    public async Task PostAsync_WhenValidatorThrows_EmitsStartedAndError_NotCompletedOrNoOp()
    {
        var sink = new TestLogSink();
        using var host = CreateHostWithLogSink(Fixture.ConnectionString, sink);
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "LOG-POST-ERR");

        Func<Task> act = () => PostInvalidTwoDaysAsync(host, docId, baseDateUtc: dateUtc, amount: 10m);

        await act.Should().ThrowAsync<NgbArgumentInvalidException>();

        sink.Records.Should().Contain(r => r.EventId.Id == 1000 && r.Message.Contains("Post", StringComparison.Ordinal));

        sink.Records.Should().Contain(r =>
            r.Level >= LogLevel.Error &&
            r.Message.Contains("Post failed", StringComparison.Ordinal));

        sink.Records.Should().NotContain(r => r.EventId.Id == 1001 && r.Message.Contains("Post", StringComparison.Ordinal));
        sink.Records.Should().NotContain(r => r.EventId.Id == 1002 && r.Message.Contains("Post", StringComparison.Ordinal));

        // And the document must stay Draft.
        await using var scope = host.Services.CreateAsyncScope();
        var doc = await scope.ServiceProvider.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
        doc!.Status.Should().Be(DocumentStatus.Draft);
    }

    [Fact]
    public async Task UnpostAsync_OnSuccess_EmitsStartedAndCompleted_NotNoOpOrError()
    {
        var sink = new TestLogSink();
        using var host = CreateHostWithLogSink(Fixture.ConnectionString, sink);
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 16, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "LOG-UNPOST-1");
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 50m);

        await UnpostAsync(host, docId);

        sink.Records.Should().Contain(r => r.EventId.Id == 1000 && r.Message.Contains("Unpost", StringComparison.Ordinal));
        sink.Records.Should().Contain(r => r.EventId.Id == 1001 && r.Message.Contains("Unpost", StringComparison.Ordinal));

        sink.Records.Should().NotContain(r => r.EventId.Id == 1002 && r.Message.Contains("Unpost", StringComparison.Ordinal));
        sink.Records.Should().NotContain(r => r.Level >= LogLevel.Error && r.Message.Contains("Unpost failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnpostAsync_WhenAlreadyDraft_EmitsStartedAndNoOp_NotCompleted()
    {
        var sink = new TestLogSink();
        using var host = CreateHostWithLogSink(Fixture.ConnectionString, sink);

        var dateUtc = new DateTime(2026, 1, 16, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "LOG-UNPOST-2");

        await UnpostAsync(host, docId);

        sink.Records.Should().Contain(r => r.EventId.Id == 1000 && r.Message.Contains("Unpost", StringComparison.Ordinal));
        sink.Records.Should().Contain(r => r.EventId.Id == 1002 && r.Message.Contains("Unpost", StringComparison.Ordinal));
        sink.Records.Should().NotContain(r => r.EventId.Id == 1001 && r.Message.Contains("Unpost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepostAsync_Twice_SecondCallEmitsNoOp_NotCompleted()
    {
        var sink = new TestLogSink();
        using var host = CreateHostWithLogSink(Fixture.ConnectionString, sink);
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 17, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, dateUtc, number: "LOG-REPOST-1");
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 100m);

        await RepostAsync(host, docId, dateUtc, amount: 101m);

        // Act: second call must be idempotent and logged as NoOp.
        await RepostAsync(host, docId, dateUtc, amount: 999m);

        sink.Records.Should().Contain(r => r.EventId.Id == 1002 && r.Message.Contains("Repost", StringComparison.Ordinal));

        // Completed is expected only once (the first successful repost).
        sink.Records.Count(r => r.EventId.Id == 1001 && r.Message.Contains("Repost", StringComparison.Ordinal))
            .Should().Be(1);
    }

    private static IHost CreateHostWithLogSink(string connectionString, TestLogSink sink)
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
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
            })
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = true;
            })
            .Build();
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
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

    private static async Task PostCashRevenueAsync(IHost host, Guid documentId, DateTime dateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var docs = sp.GetRequiredService<IDocumentPostingService>();

        await docs.PostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            ctx.Post(
                documentId: documentId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);
        }, CancellationToken.None);
    }

    private static async Task PostInvalidTwoDaysAsync(IHost host, Guid documentId, DateTime baseDateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var docs = sp.GetRequiredService<IDocumentPostingService>();

        await docs.PostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            // Produce two entries on different UTC days => validator must throw.
            ctx.Post(
                documentId: documentId,
                period: baseDateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);

            ctx.Post(
                documentId: documentId,
                period: baseDateUtc.AddDays(1),
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);
        }, CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await docs.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostAsync(IHost host, Guid documentId, DateTime dateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var docs = sp.GetRequiredService<IDocumentPostingService>();

        await docs.RepostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            ctx.Post(
                documentId: documentId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);
        }, CancellationToken.None);
    }
}
