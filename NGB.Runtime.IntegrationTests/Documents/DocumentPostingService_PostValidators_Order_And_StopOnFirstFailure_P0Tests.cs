using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Validation;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_PostValidators_Order_And_StopOnFirstFailure_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCodeOrder = "it_doc_postval_order";
    private const string TypeCodeStop = "it_doc_postval_stop";

    [Fact]
    public async Task PostAsync_ExecutesPostValidators_BeforePostingAction()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocOrderContributor>();
                services.AddSingleton<PostValidatorProbe>();
                services.AddScoped<ItOrderPostValidator>();
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var date = new DateTime(2026, 01, 10, 0, 0, 0, DateTimeKind.Utc);
        Guid id;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCodeOrder, number: null, dateUtc: date, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var probe = scope.ServiceProvider.GetRequiredService<PostValidatorProbe>();

            await posting.PostAsync(
                id,
                async (ctx, ct) =>
                {
                    probe.PostingActionCalls++;

                    if (!probe.OrderValidatorRan)
                        throw new NotSupportedException("post validators must run before posting action");

                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(id, date, chart.Get("50"), chart.Get("90.1"), 10m);
                },
                CancellationToken.None);

            probe.OrderValidatorCalls.Should().Be(1);
            probe.PostingActionCalls.Should().Be(1);
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var doc = await conn.QuerySingleAsync<(short Status, DateTime? PostedAtUtc)>(
            "SELECT status AS Status, posted_at_utc AS PostedAtUtc FROM documents WHERE id = @id;",
            new { id });

        doc.Status.Should().Be((short)DocumentStatus.Posted);
        doc.PostedAtUtc.Should().NotBeNull();

        var postingLogCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id AND operation = 1;",
            new { id });

        postingLogCount.Should().Be(1);
    }

    [Fact]
    public async Task PostAsync_WhenFirstPostValidatorFails_DoesNotExecuteSubsequentValidators_AndDoesNotRunPostingAction()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItDocStopContributor>();
                services.AddSingleton<PostValidatorProbe>();
                services.AddScoped<ItStopFirstFailingPostValidator>();
                services.AddScoped<ItStopSecondPostValidator>();
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var date = new DateTime(2026, 01, 11, 0, 0, 0, DateTimeKind.Utc);
        Guid id;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            id = await drafts.CreateDraftAsync(TypeCodeStop, number: null, dateUtc: date, manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            var probe = scope.ServiceProvider.GetRequiredService<PostValidatorProbe>();

            var act = () => posting.PostAsync(
                id,
                async (ctx, ct) =>
                {
                    probe.PostingActionCalls++;
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(id, date, chart.Get("50"), chart.Get("90.1"), 10m);
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated first post validator failure*");

            probe.FirstFailingValidatorCalls.Should().Be(1);
            probe.SecondValidatorCalls.Should().Be(0, "subsequent post validators must not run after a failure");
            probe.PostingActionCalls.Should().Be(0, "posting action must not run if post validation fails");
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var doc = await conn.QuerySingleAsync<(short Status, DateTime? PostedAtUtc)>(
            "SELECT status AS Status, posted_at_utc AS PostedAtUtc FROM documents WHERE id = @id;",
            new { id });

        doc.Status.Should().Be((short)DocumentStatus.Draft);
        doc.PostedAtUtc.Should().BeNull();

        var postingLogCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id AND operation = 1;",
            new { id });

        postingLogCount.Should().Be(0);
    }

    private sealed class ItDocOrderContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCodeOrder, d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCodeOrder,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc PostValidator Order"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .AddPostValidator<ItOrderPostValidator>());
        }
    }

    private sealed class ItDocStopContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCodeStop, d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCodeStop,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc PostValidator Stop"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .AddPostValidator<ItStopFirstFailingPostValidator>()
                .AddPostValidator<ItStopSecondPostValidator>());
        }
    }

    private sealed class PostValidatorProbe
    {
        public int OrderValidatorCalls { get; set; }
        public bool OrderValidatorRan { get; set; }

        public int FirstFailingValidatorCalls { get; set; }
        public int SecondValidatorCalls { get; set; }

        public int PostingActionCalls { get; set; }
    }

    private sealed class ItOrderPostValidator(PostValidatorProbe probe) : IDocumentPostValidator
    {
        public string TypeCode => TypeCodeOrder;

        public Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
        {
            probe.OrderValidatorCalls++;
            probe.OrderValidatorRan = true;
            return Task.CompletedTask;
        }
    }

    private sealed class ItStopFirstFailingPostValidator(PostValidatorProbe probe) : IDocumentPostValidator
    {
        public string TypeCode => TypeCodeStop;

        public Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
        {
            probe.FirstFailingValidatorCalls++;
            throw new NotSupportedException("simulated first post validator failure");
        }
    }

    private sealed class ItStopSecondPostValidator(PostValidatorProbe probe) : IDocumentPostValidator
    {
        public string TypeCode => TypeCodeStop;

        public Task ValidateBeforePostAsync(DocumentRecord documentForUpdate, CancellationToken ct)
        {
            probe.SecondValidatorCalls++;
            return Task.CompletedTask;
        }
    }
}
