using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Posting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(DocumentsPostgresCollection.Name)]
public sealed class DocumentDraftVersionTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_draft_version";
    private static readonly DateTime Date = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("field")]
    [InlineData("parts")]
    [InlineData("header")]
    public async Task ChangedDraft_RejectsStalePost_AndAllowsPostAfterReload(string change)
    {
        using var host = CreateHost();
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        var id = await CreateDocumentAsync(host);
        await using var first = host.Services.CreateAsyncScope();
        var queries = first.ServiceProvider.GetRequiredService<IDocumentActionQueryService>();
        var dispatcher = first.ServiceProvider.GetRequiredService<IDocumentActionDispatcher>();
        var seen = await queries.GetEditorStateAsync(TypeCode, id, default);

        await using (var second = host.Services.CreateAsyncScope())
        {
            if (change == "header")
            {
                await second.ServiceProvider.GetRequiredService<IDocumentDraftService>()
                    .UpdateDraftAsync(id, null, Date.AddMonths(1));
            }
            else
            {
                var payload = change == "field"
                    ? new RecordPayload(Fields: Fields(("amount", 500m)))
                    : new RecordPayload(Parts: new Dictionary<string, RecordPartPayload>
                    {
                        ["lines"] = new([Fields(("ordinal", 1), ("amount", 400m))])
                    });
                await second.ServiceProvider.GetRequiredService<IDocumentService>()
                    .UpdateDraftAsync(TypeCode, id, payload, default);
            }
        }

        var stale = () => dispatcher.ExecuteAsync(TypeCode, id, StandardDocumentActionCodes.Post,
            Guid.NewGuid().ToString(), new ExecuteDocumentActionRequestDto(seen.DocumentVersion), default);
        await stale.Should().ThrowAsync<DocumentVersionConflictException>();

        var uow = first.ServiceProvider.GetRequiredService<IUnitOfWork>();
        (await uow.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @id;", new { id })).Should().Be(0);
        var current = await queries.GetEditorStateAsync(TypeCode, id, default);
        current.Document.Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.Draft);
        current.DocumentVersion.Should().Be(seen.DocumentVersion + 1);

        var posted = await dispatcher.ExecuteAsync(TypeCode, id, StandardDocumentActionCodes.Post,
            Guid.NewGuid().ToString(), new ExecuteDocumentActionRequestDto(current.DocumentVersion), default);
        posted.DocumentVersion.Should().Be(current.DocumentVersion + 1);
        posted.Document.Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.Posted);
        (await uow.Connection.ExecuteScalarAsync<decimal>(
            "SELECT SUM(amount) FROM accounting_register_main WHERE document_id = @id;", new { id }))
            .Should().Be(change == "header" ? 100m : 500m);
    }

    [Fact]
    public async Task HeaderUpdate_NoOpPreservesVersion_AndExternalRollbackRestoresVersion()
    {
        using var host = CreateHost();
        var id = await CreateDocumentAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var before = (await documents.GetAsync(id))!;

        (await drafts.UpdateDraftAsync(id, before.Number, before.DateUtc)).Should().BeFalse();
        (await documents.GetAsync(id))!.Version.Should().Be(before.Version);

        await uow.BeginTransactionAsync();
        (await drafts.UpdateDraftAsync(id, "CHANGED", null, manageTransaction: false)).Should().BeTrue();
        (await documents.GetAsync(id))!.Version.Should().Be(before.Version + 1);
        await uow.RollbackAsync();

        var restored = (await documents.GetAsync(id))!;
        restored.Version.Should().Be(before.Version);
        restored.Number.Should().Be(before.Number);
    }

    [Fact]
    public async Task FailedPartReplacement_RollsBackHeadAndVersion()
    {
        using var host = CreateHost();
        var id = await CreateDocumentAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var before = (await documents.GetAsync(id))!;
        var service = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var payload = new RecordPayload(Fields: Fields(("amount", 500m)),
            Parts: new Dictionary<string, RecordPartPayload>
            {
                ["lines"] = new([Fields(("ordinal", 1), ("amount", -1m))])
            });

        await ((Func<Task>)(() => service.UpdateDraftAsync(TypeCode, id, payload, default)))
            .Should().ThrowAsync<Npgsql.PostgresException>();

        (await documents.GetAsync(id))!.Version.Should().Be(before.Version);
        var document = await service.GetByIdAsync(TypeCode, id, default);
        document.Payload.Fields!["amount"].GetDecimal().Should().Be(100m);
        document.Payload.Parts!["lines"].Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GeneralJournalEntry_DraftHeaderAndLinesAdvanceVersion()
    {
        using var host = CreateHost();
        var (cash, revenue, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var id = await service.CreateDraftAsync(Date, "version-test");
        var initial = (await documents.GetAsync(id))!.Version;

        await service.UpdateDraftHeaderAsync(id,
            new GeneralJournalEntryDraftHeaderUpdate(null, "ADJUSTMENT", "Draft edit", null, false, null),
            "version-test");
        (await documents.GetAsync(id))!.Version.Should().Be(initial + 1);
        await service.ReplaceDraftLinesAsync(id,
            [new(GeneralJournalEntryModels.LineSide.Debit, cash, 100m, Memo: null),
                new(GeneralJournalEntryModels.LineSide.Credit, revenue, 100m, Memo: null)], "version-test");
        (await documents.GetAsync(id))!.Version.Should().Be(initial + 2);
    }

    private IHost CreateHost()
        => IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, VersionDocument>();
            services.AddSingleton<IPermissionSnapshotProvider, AdministratorSnapshot>();
            services.AddScoped<AmountPosting>();
        });

    private static async Task<Guid> CreateDocumentAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var id = await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>()
            .CreateDraftAsync(TypeCode, "VERSION", Date);
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync();
        await uow.Connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS it_draft_version (
                document_id uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
                display text NOT NULL,
                amount numeric(18,4) NOT NULL
            );
            CREATE TABLE IF NOT EXISTS it_draft_version__lines (
                document_id uuid NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                ordinal integer NOT NULL,
                amount numeric(18,4) NOT NULL CHECK (amount >= 0),
                PRIMARY KEY (document_id, ordinal)
            );
            INSERT INTO it_draft_version(document_id, display, amount) VALUES (@id, 'Version test', 100);
            """, new { id });
        return id;
    }

    private static IReadOnlyDictionary<string, JsonElement> Fields(params (string Name, object Value)[] fields)
        => fields.ToDictionary(field => field.Name, field => JsonSerializer.SerializeToElement(field.Value));

    private sealed class AdministratorSnapshot : IPermissionSnapshotProvider
    {
        public Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct)
            => Task.FromResult(new PermissionSnapshot(null, "version-test-admin", true, true, true, 1, []));
    }

    private sealed class VersionDocument : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, document => document
                .Metadata(new DocumentTypeMetadata(TypeCode,
                    [
                        new DocumentTableMetadata("it_draft_version", TableKind.Head,
                        [
                            new("document_id", ColumnType.Guid, Required: true),
                            new("display", ColumnType.String, Required: true),
                            new("amount", ColumnType.Decimal, Required: true)
                        ]),
                        new DocumentTableMetadata("it_draft_version__lines", TableKind.Part,
                        [
                            new("document_id", ColumnType.Guid, Required: true),
                            new("ordinal", ColumnType.Int32, Required: true),
                            new("amount", ColumnType.Decimal, Required: true)
                        ], PartCode: "lines")
                    ],
                    new DocumentPresentationMetadata("Draft version test"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .PostingHandler<AmountPosting>());
        }
    }

    private sealed class AmountPosting(IUnitOfWork uow) : IDocumentPostingHandler
    {
        public string TypeCode => DocumentDraftVersionTests.TypeCode;

        public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext context, CancellationToken ct)
        {
            var amount = await uow.Connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                """
                SELECT amount + COALESCE((SELECT SUM(amount) FROM it_draft_version__lines WHERE document_id = @Id), 0)
                FROM it_draft_version WHERE document_id = @Id;
                """, new { document.Id }, transaction: uow.Transaction, cancellationToken: ct));
            var chart = await context.GetChartOfAccountsAsync(ct);
            context.Post(document.Id, document.DateUtc, chart.Get("50"), chart.Get("90.1"), amount);
        }
    }
}
