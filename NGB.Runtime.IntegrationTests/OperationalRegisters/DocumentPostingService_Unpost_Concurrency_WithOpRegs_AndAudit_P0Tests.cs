using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Concurrent Unpost calls for the SAME document must be idempotent across the whole platform composition:
/// - Accounting: exactly one storno entry (no duplicates) + one posting_log row for Unpost
/// - Operational Registers: exactly one storno movement (no duplicates) + one write_log row for Unpost + dirty month not duplicated
/// - AuditLog: exactly one document.unpost event (no duplicates), actor upsert is not duplicated
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_Unpost_Concurrency_WithOpRegs_AndAudit_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string ActorSubject = "kc|unpost-concurrent-opreg";

    private static readonly Guid BuildingsDimensionId = DeterministicGuid.Create("Dimension|buildings");
    private static readonly Guid BuildingsValueId = DeterministicGuid.Create("DimensionValue|buildings|b1");

    [Fact]
    public async Task Unpost_ConcurrentCalls_WithOpRegs_WritesSingleStornoAccountingOpRegAndAudit()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(dateUtc.Year, dateUtc.Month, 1);

        var regCode = UniqueRegisterCode();
        var registerId = await CreateRegisterAsync(host, regCode);
        await ConfigureRegisterAsync(host, registerId, dimensionId: BuildingsDimensionId, dimensionCode: "Buildings");
        await ConfigureHandlerStateAsync(host, regCode, amount: 10m);

        var docId = await CreateDraftAsync(host, dateUtc, number: "IT-UNPOST-CONCURRENT-OPREG-0001");

        // First post (single-thread) to create the ground truth movements and accounting entry.
        await PostOnceAsync(host, docId, dateUtc);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var docsRepo = sp.GetRequiredService<IDocumentRepository>();
        var doc = await docsRepo.GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Posted);

        var uow = sp.GetRequiredService<IUnitOfWork>();
        var regs = sp.GetRequiredService<IOperationalRegisterRepository>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var reg = await regs.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();
        var movementsTable = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

        // Sanity: after Post we have exactly one non-storno movement and one accounting entry.
        var m0 = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT count(*) FROM {movementsTable} WHERE document_id = @D AND is_storno = FALSE;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
        m0.Should().Be(1);

        var a0 = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM accounting_register_main WHERE document_id = @D AND is_storno = FALSE;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
        a0.Should().Be(1);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var t1 = Task.Run(() => RunUnpostAsync(host, docId, gate));
        var t2 = Task.Run(() => RunUnpostAsync(host, docId, gate));

        gate.SetResult(true);

        var errors = await Task.WhenAll(t1, t2)
            .WaitAsync(TimeSpan.FromSeconds(45));

        errors.All(e => e is null).Should().BeTrue("UnpostAsync must be idempotent under concurrency; one call unposts, the other becomes a no-op");

        // Reload document and validate final state.
        doc = await docsRepo.GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Draft);
        doc.PostedAtUtc.Should().BeNull();

        // Accounting: exactly one storno entry appended.
        var accNonStorno = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM accounting_register_main WHERE document_id = @D AND is_storno = FALSE;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
        var accStorno = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM accounting_register_main WHERE document_id = @D AND is_storno = TRUE;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        accNonStorno.Should().Be(1);
        accStorno.Should().Be(1, "concurrent Unpost must append storno exactly once");

        // Posting log: exactly one completed Unpost row.
        var unpostLogCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM accounting_posting_state WHERE document_id = @D AND operation = 2 AND completed_at_utc IS NOT NULL;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
        unpostLogCount.Should().Be(1);

        // OpReg write_log: exactly one completed Unpost row.
        var opregUnpostLogCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_write_state
            WHERE register_id = @R AND document_id = @D AND operation = @O AND completed_at_utc IS NOT NULL;
            """,
            new { R = registerId, D = docId, O = (short)OperationalRegisterWriteOperation.Unpost },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        opregUnpostLogCount.Should().Be(1);

        // OpReg movements: exactly one storno movement appended.
        var opregNonStorno = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT count(*) FROM {movementsTable} WHERE document_id = @D AND is_storno = FALSE;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
        var opregStorno = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT count(*) FROM {movementsTable} WHERE document_id = @D AND is_storno = TRUE;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        opregNonStorno.Should().Be(1);
        opregStorno.Should().Be(1, "concurrent Unpost must append storno movement exactly once");

        // Dirty month should exist exactly once (it is created on Post and must not duplicate on Unpost).
        var dirtyCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_finalizations
            WHERE register_id = @R AND period = @P AND status = 2;
            """,
            new { R = registerId, P = period },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        dirtyCount.Should().Be(1);

        // Audit: exactly one document.unpost event for this document.
        var auditReader = sp.GetRequiredService<IAuditEventReader>();
        var unpostEvents = await auditReader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: docId,
                ActionCode: AuditActionCodes.DocumentUnpost,
                Limit: 50,
                Offset: 0),
            CancellationToken.None);

        unpostEvents.Should().HaveCount(1, "concurrent duplicate Unpost calls must emit exactly one document.unpost audit event");

        // Actor upsert should not duplicate.
        var users = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM platform_users WHERE auth_subject = @S;",
            new { S = ActorSubject },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
        users.Should().Be(1);
    }

    private IHost CreateHost()
    {
        return IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItOpregUnpostTestDocumentContributor>();
                services.AddSingleton<ItOpregUnpostTestState>();

                services.AddScoped<ItOpregUnpostTestOperationalRegisterPostingHandler>();

                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: ActorSubject,
                        Email: "unpost.concurrent.opreg@example.com",
                        DisplayName: "Unpost Concurrent OpReg")));
            });
    }

    private static string UniqueRegisterCode() => "rr_" + Guid.CreateVersion7().ToString("N")[..8];

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

    private static async Task<Guid> CreateRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        return await mgmt.UpsertAsync(code, name: "IT Register", CancellationToken.None);
    }

    private static async Task ConfigureRegisterAsync(IHost host, Guid registerId, Guid dimensionId, string dimensionCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

        await mgmt.ReplaceResourcesAsync(registerId,
            [new OperationalRegisterResourceDefinition("amount", "Amount", 1)],
            CancellationToken.None);

        await mgmt.ReplaceDimensionRulesAsync(registerId,
            [new OperationalRegisterDimensionRule(dimensionId, dimensionCode, 1, true)],
            CancellationToken.None);
    }

    private static async Task ConfigureHandlerStateAsync(IHost host, string registerCode, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<ItOpregUnpostTestState>();

        state.RegisterCode = registerCode;
        state.Amount = amount;
        state.DimensionId = BuildingsDimensionId;
        state.ValueId = BuildingsValueId;
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, DateTime dateUtc, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: "it_doc_opreg_unpost",
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task PostOnceAsync(IHost host, Guid docId, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.PostAsync(docId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            ctx.Post(
                documentId: docId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: 1m);
        }, CancellationToken.None);
    }

    private static async Task<Exception?> RunUnpostAsync(IHost host, Guid docId, TaskCompletionSource<bool> gate)
    {
        try
        {
            await gate.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.UnpostAsync(docId, CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed class ItOpregUnpostTestState
    {
        public string RegisterCode { get; set; } = "";
        public decimal Amount { get; set; }
        public Guid DimensionId { get; set; }
        public Guid ValueId { get; set; }
    }

    private sealed class ItOpregUnpostTestDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_doc_opreg_unpost", b => b
                .Metadata(new DocumentTypeMetadata(
                    "it_doc_opreg_unpost",
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Document OpReg Unpost"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .OperationalRegisterPostingHandler<ItOpregUnpostTestOperationalRegisterPostingHandler>());
        }
    }

    private sealed class ItOpregUnpostTestOperationalRegisterPostingHandler(
        ItOpregUnpostTestState state,
        IDimensionSetService dimensionSets)
        : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => "it_doc_opreg_unpost";

        public async Task BuildMovementsAsync(
            DocumentRecord document,
            IOperationalRegisterMovementsBuilder builder,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(state.RegisterCode))
                throw new XunitException("Test state RegisterCode is not configured.");

            var bag = new DimensionBag([
                new DimensionValue(state.DimensionId, state.ValueId)
            ]);

            var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(bag, ct);

            builder.Add(
                state.RegisterCode,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: document.DateUtc,
                    DimensionSetId: dimensionSetId,
                    Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["amount"] = state.Amount
                    }));
        }
    }
}
