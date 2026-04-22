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
/// P0: Concurrent Post calls for the SAME document must be idempotent across the whole platform composition:
/// - Accounting: one register entry + one posting_log row
/// - Operational Registers: one movement + one write_log row + dirty month
/// - AuditLog: exactly one document.post event (no duplicates), actor upsert is performed once
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_Post_Concurrency_WithOpRegs_AndAudit_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string ActorSubject = "kc|post-concurrent-opreg";

    private static readonly Guid BuildingsDimensionId = DeterministicGuid.Create("Dimension|buildings");
    private static readonly Guid BuildingsValueId = DeterministicGuid.Create("DimensionValue|buildings|b1");

    [Fact]
    public async Task Post_ConcurrentCalls_WithOpRegs_WritesSingleAccountingOpRegAndAudit()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);

        var regCode = UniqueRegisterCode();
        var registerId = await CreateRegisterAsync(host, regCode);
        await ConfigureRegisterAsync(host, registerId, dimensionId: BuildingsDimensionId, dimensionCode: "Buildings");

        await ConfigureHandlerStateAsync(host, regCode, amount: 10m);

        var docId = await CreateDraftAsync(host, dateUtc, number: "IT-POST-CONCURRENT-OPREG-0001");

        var baseline = await CaptureBaselineAsync();

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var t1 = Task.Run(() => RunPostAsync(host, docId, dateUtc, gate));
        var t2 = Task.Run(() => RunPostAsync(host, docId, dateUtc, gate));

        gate.SetResult(true);

        var errors = await Task.WhenAll(t1, t2)
            .WaitAsync(TimeSpan.FromSeconds(45));

        errors.All(e => e is null).Should().BeTrue("PostAsync must be idempotent under concurrency; one call posts, the other becomes a no-op");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var docsRepo = sp.GetRequiredService<IDocumentRepository>();
        var doc = await docsRepo.GetAsync(docId, CancellationToken.None);
        doc.Should().NotBeNull();
        doc!.Status.Should().Be(DocumentStatus.Posted);
        doc.PostedAtUtc.Should().NotBeNull();

        var uow = sp.GetRequiredService<IUnitOfWork>();
        var regs = sp.GetRequiredService<IOperationalRegisterRepository>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        // Accounting: exactly one entry for this document.
        var accCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM accounting_register_main WHERE document_id = @D;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        accCount.Should().Be(1);

        // Posting log: exactly one completed Post row.
        var postLogCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM accounting_posting_state WHERE document_id = @D AND operation = 1 AND completed_at_utc IS NOT NULL;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        postLogCount.Should().Be(1);

        // Operational Registers: exactly one Post write_log row for this register/doc.
        var opregLogCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_write_state
            WHERE register_id = @R AND document_id = @D AND operation = @O AND completed_at_utc IS NOT NULL;
            """,
            new { R = registerId, D = docId, O = (short)OperationalRegisterWriteOperation.Post },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        opregLogCount.Should().Be(1);

        // Operational Registers: exactly one non-storno movement row for this document.
        var reg = await regs.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();
        var movementsTable = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

        var movementCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT count(*) FROM {movementsTable} WHERE document_id = @D AND is_storno = FALSE;",
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        movementCount.Should().Be(1);

        // Dirty month marked exactly once.
        var period = new DateOnly(dateUtc.Year, dateUtc.Month, 1);
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

        // Audit: exactly one document.post event for this document.
        var auditReader = sp.GetRequiredService<IAuditEventReader>();
        var postEvents = await auditReader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.Document,
                EntityId: docId,
                ActionCode: AuditActionCodes.DocumentPost,
                Limit: 50,
                Offset: 0),
            CancellationToken.None);

        postEvents.Should().HaveCount(1, "concurrent duplicate Post calls must emit exactly one document.post audit event");

        // Baseline-based global sanity: audit events should increase by exactly 1 (the document.post).
        var after = await CaptureBaselineAsync();
        after.AuditEvents.Should().Be(baseline.AuditEvents + 1);

        // Actor upsert should happen exactly once for this subject.
        after.UsersForActorSubject.Should().Be(1);
    }

    private sealed record Baseline(int AuditEvents, int UsersForActorSubject);

    private async Task<Baseline> CaptureBaselineAsync()
    {
        await using var scope = hostForBaseline.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var auditEvents = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM platform_audit_events;",
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        var users = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM platform_users WHERE auth_subject = @S;",
            new { S = ActorSubject },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        return new Baseline(auditEvents, users);
    }

    // We need a stable host reference for CaptureBaselineAsync; the test creates the host once.
    private IHost hostForBaseline = null!;

    private IHost CreateHost()
    {
        hostForBaseline = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItOpregTestDocumentContributor>();
                services.AddSingleton<ItOpregTestState>();
                services.AddScoped<ItOpregTestOperationalRegisterPostingHandler>();

                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: ActorSubject,
                        Email: "post.concurrent.opreg@example.com",
                        DisplayName: "Post Concurrent OpReg")));
            });

        return hostForBaseline;
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
        var state = scope.ServiceProvider.GetRequiredService<ItOpregTestState>();

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
            typeCode: "it_doc_opreg",
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task<Exception?> RunPostAsync(IHost host, Guid docId, DateTime dateUtc, TaskCompletionSource<bool> gate)
    {
        try
        {
            await gate.Task;
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

    private sealed class ItOpregTestState
    {
        public string RegisterCode { get; set; } = "";
        public decimal Amount { get; set; }
        public Guid DimensionId { get; set; }
        public Guid ValueId { get; set; }
    }

    private sealed class ItOpregTestDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_doc_opreg", b => b
                .Metadata(new DocumentTypeMetadata(
                    "it_doc_opreg",
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Document Operational Registers"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .OperationalRegisterPostingHandler<ItOpregTestOperationalRegisterPostingHandler>());
        }
    }

    private sealed class ItOpregTestOperationalRegisterPostingHandler(
        ItOpregTestState state,
        IDimensionSetService dimensionSets)
        : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => "it_doc_opreg";

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
