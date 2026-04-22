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
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Concurrent Repost calls for the SAME document must be idempotent across the whole platform composition:
/// - Accounting: exactly one storno entry + exactly one new entry + one posting_log row for Repost
/// - Operational Registers: idempotent storno+new movements across ALL affected registers (union of old+new)
/// - AuditLog: exactly one document.repost event (no duplicates), actor upsert is not duplicated
///
/// Additionally, this test covers the important "union" behavior for repost:
/// - movements may move from one register to another between Post and Repost
/// - the old register must receive storno (to cancel previous state)
/// - the new register must receive the new state
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_Repost_Concurrency_WithOpRegs_AndAudit_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string ActorSubject = "kc|repost-concurrent-opreg";

    private static readonly Guid BuildingsDimensionId = DeterministicGuid.Create("Dimension|buildings");
    private static readonly Guid BuildingsValueId = DeterministicGuid.Create("DimensionValue|buildings|b1");

    [Fact]
    public async Task Repost_ConcurrentCalls_WithOpRegs_UnionOldAndNewRegisters_IsIdempotent()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateOnly(dateUtc.Year, dateUtc.Month, 1);

        // Two registers to validate the union(old+new) semantics.
        var regCodeOld = UniqueRegisterCode("old");
        var regCodeNew = UniqueRegisterCode("new");

        var registerOldId = await CreateRegisterAsync(host, regCodeOld);
        var registerNewId = await CreateRegisterAsync(host, regCodeNew);

        await ConfigureRegisterAsync(host, registerOldId, dimensionId: BuildingsDimensionId, dimensionCode: "Buildings");
        await ConfigureRegisterAsync(host, registerNewId, dimensionId: BuildingsDimensionId, dimensionCode: "Buildings");

        var docId = await CreateDraftAsync(host, dateUtc, number: "IT-REPOST-CONCURRENT-OPREG-0001");

        // Initial Post writes to the OLD register.
        await ConfigureHandlerStateAsync(host, regCodeOld, amount: 10m);
        await PostOnceAsync(host, docId, dateUtc, amount: 1m);

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var docsRepo = sp.GetRequiredService<IDocumentRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var regs = sp.GetRequiredService<IOperationalRegisterRepository>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var regOld = await regs.GetByIdAsync(registerOldId, CancellationToken.None);
        regOld.Should().NotBeNull();
        var oldMovementsTable = OperationalRegisterNaming.MovementsTable(regOld!.TableCode);

        var regNew = await regs.GetByIdAsync(registerNewId, CancellationToken.None);
        regNew.Should().NotBeNull();
        var newMovementsTable = OperationalRegisterNaming.MovementsTable(regNew!.TableCode);

        // Sanity: after Post we have exactly one non-storno movement in OLD register and none in NEW.
        var oldNonStorno0 = await CountMovementsOrZeroAsync(
            uow,
            oldMovementsTable,
            docId,
            isStorno: false,
            CancellationToken.None);

        var newNonStorno0 = await CountMovementsOrZeroAsync(
            uow,
            newMovementsTable,
            docId,
            isStorno: false,
            CancellationToken.None);

        oldNonStorno0.Should().Be(1);
        newNonStorno0.Should().Be(0);

        // Now change the OpReg handler state so Repost targets the NEW register.
        await ConfigureHandlerStateAsync(host, regCodeNew, amount: 20m);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var t1 = Task.Run(() => RunRepostAsync(host, docId, dateUtc, gate, amount: 2m));
        var t2 = Task.Run(() => RunRepostAsync(host, docId, dateUtc, gate, amount: 2m));

        gate.SetResult(true);

        var errors = await Task.WhenAll(t1, t2)
            .WaitAsync(TimeSpan.FromSeconds(45));

        errors.All(e => e is null).Should().BeTrue("RepostAsync must be idempotent under concurrency; one call reposts, the other becomes a no-op");

        // Reload document and validate final state.
        var doc = await docsRepo.GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Posted);
        doc.PostedAtUtc.Should().NotBeNull();

        // Accounting: initial non-storno + new non-storno, and exactly one storno.
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

        accNonStorno.Should().Be(2);
        accStorno.Should().Be(1, "concurrent Repost must append storno exactly once");

        // Posting log: exactly one completed Post and one completed Repost.
        var postLogCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM accounting_posting_state WHERE document_id = @D AND operation = 1 AND completed_at_utc IS NOT NULL;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        var repostLogCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM accounting_posting_state WHERE document_id = @D AND operation = 3 AND completed_at_utc IS NOT NULL;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        postLogCount.Should().Be(1);
        repostLogCount.Should().Be(1);

        // Operational Registers: OLD register receives storno (to cancel the previous state) but no new movement.
        var oldNonStorno = await CountMovementsOrZeroAsync(
            uow,
            oldMovementsTable,
            docId,
            isStorno: false,
            CancellationToken.None);

        var oldStorno = await CountMovementsOrZeroAsync(
            uow,
            oldMovementsTable,
            docId,
            isStorno: true,
            CancellationToken.None);

        oldNonStorno.Should().Be(1);
        oldStorno.Should().Be(1, "OLD register must receive exactly one storno on Repost");

        // Operational Registers: NEW register receives the new state (one non-storno movement), no storno.
        var newNonStorno = await CountMovementsOrZeroAsync(
            uow,
            newMovementsTable,
            docId,
            isStorno: false,
            CancellationToken.None);

        var newStorno = await CountMovementsOrZeroAsync(
            uow,
            newMovementsTable,
            docId,
            isStorno: true,
            CancellationToken.None);

        newNonStorno.Should().Be(1);
        newStorno.Should().Be(0);

        // OpReg write_log: idempotent Repost entries across BOTH registers.
        var opregOldPostLog = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM operational_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O AND completed_at_utc IS NOT NULL;",
            new { R = registerOldId, D = docId, O = (short)OperationalRegisterWriteOperation.Post },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        var opregOldRepostLog = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM operational_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O AND completed_at_utc IS NOT NULL;",
            new { R = registerOldId, D = docId, O = (short)OperationalRegisterWriteOperation.Repost },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        var opregNewPostLog = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM operational_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O AND completed_at_utc IS NOT NULL;",
            new { R = registerNewId, D = docId, O = (short)OperationalRegisterWriteOperation.Post },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        var opregNewRepostLog = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM operational_register_write_state WHERE register_id = @R AND document_id = @D AND operation = @O AND completed_at_utc IS NOT NULL;",
            new { R = registerNewId, D = docId, O = (short)OperationalRegisterWriteOperation.Repost },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        opregOldPostLog.Should().Be(1);
        opregOldRepostLog.Should().Be(1);
        opregNewPostLog.Should().Be(0);
        opregNewRepostLog.Should().Be(1);

        // Dirty months: OLD was already dirty after Post; NEW becomes dirty on Repost. Both must exist once.
        var dirtyOld = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM operational_register_finalizations WHERE register_id = @R AND period = @P AND status = 2;",
            new { R = registerOldId, P = period },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        var dirtyNew = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM operational_register_finalizations WHERE register_id = @R AND period = @P AND status = 2;",
            new { R = registerNewId, P = period },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        dirtyOld.Should().Be(1);
        dirtyNew.Should().Be(1);

        // Audit: exactly one document.repost event.
        var auditReader = sp.GetRequiredService<IAuditEventReader>();
        var repostEvents = await auditReader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: docId,
                ActionCode: AuditActionCodes.DocumentRepost,
                Limit: 50,
                Offset: 0),
            CancellationToken.None);

        repostEvents.Should().HaveCount(1, "concurrent duplicate Repost calls must emit exactly one document.repost audit event");

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
                services.AddSingleton<IDefinitionsContributor, ItOpregRepostTestDocumentContributor>();
                services.AddSingleton<ItOpregRepostTestState>();

                services.AddScoped<ItOpregRepostTestOperationalRegisterPostingHandler>();

                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: ActorSubject,
                        Email: "repost.concurrent.opreg@example.com",
                        DisplayName: "Repost Concurrent OpReg")));
            });
    }

    private static string UniqueRegisterCode(string suffix)
        => "rr_" + suffix + "_" + Guid.CreateVersion7().ToString("N")[..8];

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

    private static async Task<Guid> CreateDraftAsync(IHost host, DateTime dateUtc, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: "it_doc_opreg_repost",
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task ConfigureHandlerStateAsync(IHost host, string registerCode, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<ItOpregRepostTestState>();

        state.RegisterCode = registerCode;
        state.Amount = amount;
        state.DimensionId = BuildingsDimensionId;
        state.ValueId = BuildingsValueId;
    }

    private static async Task PostOnceAsync(IHost host, Guid docId, DateTime dateUtc, decimal amount)
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
                amount: amount);
        }, CancellationToken.None);
    }

    private static async Task<Exception?> RunRepostAsync(IHost host, Guid docId, DateTime dateUtc, TaskCompletionSource<bool> gate, decimal amount)
    {
        try
        {
            await gate.Task;
            await using var scope = host.Services.CreateAsyncScope();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await docs.RepostAsync(docId, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: docId,
                    period: dateUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: amount);
            }, CancellationToken.None);

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<int> CountMovementsOrZeroAsync(
        IUnitOfWork uow,
        string movementsTable,
        Guid documentId,
        bool isStorno,
        CancellationToken ct)
    {
        // OpReg per-register tables are created lazily. A register may exist without movements, and thus
        // without its movements table. In such cases we treat the count as 0.
        var exists = await uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@t) IS NOT NULL;",
            new { t = "public." + movementsTable },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (!exists)
            return 0;

        return await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT count(*) FROM {movementsTable} WHERE document_id = @D AND is_storno = @S;",
            new { D = documentId, S = isStorno },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed class ItOpregRepostTestState
    {
        public string RegisterCode { get; set; } = "";
        public decimal Amount { get; set; }
        public Guid DimensionId { get; set; }
        public Guid ValueId { get; set; }
    }

    private sealed class ItOpregRepostTestDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_doc_opreg_repost", b => b
                .Metadata(new DocumentTypeMetadata(
                    "it_doc_opreg_repost",
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Document Operational Registers Repost"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .OperationalRegisterPostingHandler<ItOpregRepostTestOperationalRegisterPostingHandler>());
        }
    }

    private sealed class ItOpregRepostTestOperationalRegisterPostingHandler(
        ItOpregRepostTestState state,
        IDimensionSetService dimensionSets)
        : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => "it_doc_opreg_repost";

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
