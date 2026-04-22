using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Accounts;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

/// <summary>
/// P1: Stress composed posting pipeline (Accounting + Operational Registers + AuditLog) under concurrency.
/// Goal: ensure lock ordering across subsystems does not deadlock and idempotency remains correct.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Composed_Posting_Accounting_Opreg_Audit_Concurrency_NoDeadlocks_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DocType = "it_doc_composed_conc";
    private const string ActorSubject = "kc|it|composed-concurrency";

    [Fact]
    public async Task Repost_ManyConcurrentCallsAcrossManyDocuments_IsIdempotent_AndDoesNotDeadlock()
    {
        // Arrange
        using var hostNoActor = CreateHost(actor: null);
        await EnsureMinimalAccountsAsync(hostNoActor);

        // Seed one Operational Register (resources + empty dimension rules) and ensure physical schema.
        var registerCode = $"rr_{Guid.CreateVersion7():N}";
        var registerId = await UpsertRegisterAsync(hostNoActor, registerCode);
        await ReplaceRegisterResourcesAsync(hostNoActor, registerId, resources:
        [
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        ]);
        await ReplaceRegisterDimensionRulesAsync(hostNoActor, registerId, newRules: Array.Empty<OperationalRegisterDimensionRule>());
        await EnsureMovementsSchemaAsync(hostNoActor, registerId);

        // The composed host uses an actor (so platform_users + audit fields are exercised).
        var state = new ItState { RegisterCode = registerCode, Amount = 10m };
        using var host = CreateHost(actor: new ActorIdentity(ActorSubject, "it@ngb.local", "IT", true), state);

        var docDateUtc = new DateTime(2026, 2, 20, 12, 0, 0, DateTimeKind.Utc);
        var postPeriodUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var docIds = new List<Guid>();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            // Create + Post baseline for multiple documents.
            for (var i = 0; i < 10; i++)
            {
                var docId = await drafts.CreateDraftAsync(
                    typeCode: DocType,
                    number: $"IT-{i + 1:000}",
                    dateUtc: docDateUtc,
                    manageTransaction: true,
                    ct: CancellationToken.None);

                docIds.Add(docId);

                await posting.PostAsync(
                    documentId: docId,
                    postingAction: (ctx, ct) => PostOneEntryAsync(ctx, docId, postPeriodUtc, amount: 1m, ct),
                    ct: CancellationToken.None);
            }
        }

        // Make the new movements different from the baseline ones.
        state.Amount = 20m;

        // Act: many concurrent Repost calls, across many documents.
        // Each doc gets multiple concurrent callers; only one should win (posting_log idempotency).
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = docIds
            .SelectMany(docId => Enumerable.Range(0, 5)
                .Select(attempt => Task.Run(async () =>
                {
                    await gate.Task;

                    // Deterministic jitter to increase interleavings without flakiness.
                    var delayMs = ((docId.GetHashCode() ^ attempt) & 0x07) * 3;
                    if (delayMs > 0)
                        await Task.Delay(delayMs, CancellationToken.None);

                    await using var scope = host.Services.CreateAsyncScope();
                    var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

                    await posting.RepostAsync(
                        documentId: docId,
                        postNew: (ctx, ct) => PostOneEntryAsync(ctx, docId, postPeriodUtc, amount: 2m, ct),
                        ct: CancellationToken.None);
                })))
            .ToArray();

        gate.SetResult();

        // If lock ordering is broken, this is where Postgres would typically throw a deadlock.
        await Task.WhenAll(tasks);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            // Movements table name for the register (table_code is collision-safe).
            var tableCode = await uow.Connection.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT table_code FROM operational_registers WHERE register_id = @RegisterId",
                new { RegisterId = registerId },
                cancellationToken: CancellationToken.None));
            var movementsTable = NGB.OperationalRegisters.OperationalRegisterNaming.MovementsTable(tableCode);

            // Actor must be upserted once (no duplicate inserts on no-op operations).
            var actorCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @Sub",
                new { Sub = ActorSubject },
                cancellationToken: CancellationToken.None));
            actorCount.Should().Be(1);

            // Register month must be Dirty (posting marks affected periods dirty).
            var dirtyCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM operational_register_finalizations
                WHERE register_id = @RegisterId
                  AND period = @Period
                  AND status = @Dirty;
                """,
                new
                {
                    RegisterId = registerId,
                    Period = new DateOnly(2026, 2, 1),
                    Dirty = (short)OperationalRegisterFinalizationStatus.Dirty
                },
                cancellationToken: CancellationToken.None));
            dirtyCount.Should().Be(1);

            foreach (var docId in docIds)
            {
                // Document remains posted.
                var status = await uow.Connection.QuerySingleAsync<short>(new CommandDefinition(
                    "SELECT status FROM documents WHERE id = @Id",
                    new { Id = docId },
                    cancellationToken: CancellationToken.None));
                status.Should().Be((short)NGB.Core.Documents.DocumentStatus.Posted);

                // Accounting: baseline post + one repost -> 2 non-storno + 1 storno.
                var accNonStorno = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @Id AND is_storno = false",
                    new { Id = docId },
                    cancellationToken: CancellationToken.None));
                var accStorno = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @Id AND is_storno = true",
                    new { Id = docId },
                    cancellationToken: CancellationToken.None));
                accNonStorno.Should().Be(2);
                accStorno.Should().Be(1);

                // Operational register movements: baseline + repost (new) + storno.
                var opregNonStorno = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    $"SELECT COUNT(*) FROM {movementsTable} WHERE document_id = @Id AND is_storno = false",
                    new { Id = docId },
                    cancellationToken: CancellationToken.None));
                var opregStorno = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    $"SELECT COUNT(*) FROM {movementsTable} WHERE document_id = @Id AND is_storno = true",
                    new { Id = docId },
                    cancellationToken: CancellationToken.None));
                opregNonStorno.Should().Be(2);
                opregStorno.Should().Be(1);

                // Accounting posting log idempotency (Post=1, Repost=1).
                var postLog = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM accounting_posting_state
                    WHERE document_id = @Id AND operation = 1 AND completed_at_utc IS NOT NULL;
                    """,
                    new { Id = docId },
                    cancellationToken: CancellationToken.None));
                var repostLog = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM accounting_posting_state
                    WHERE document_id = @Id AND operation = 3 AND completed_at_utc IS NOT NULL;
                    """,
                    new { Id = docId },
                    cancellationToken: CancellationToken.None));
                postLog.Should().Be(1);
                repostLog.Should().Be(1);

                // Operational register write log idempotency (Post=1, Repost=1).
                var opregPostLog = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM operational_register_write_state
                    WHERE register_id = @RegisterId AND document_id = @Id AND operation = 1 AND completed_at_utc IS NOT NULL;
                    """,
                    new { RegisterId = registerId, Id = docId },
                    cancellationToken: CancellationToken.None));
                var opregRepostLog = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM operational_register_write_state
                    WHERE register_id = @RegisterId AND document_id = @Id AND operation = 3 AND completed_at_utc IS NOT NULL;
                    """,
                    new { RegisterId = registerId, Id = docId },
                    cancellationToken: CancellationToken.None));
                opregPostLog.Should().Be(1);
                opregRepostLog.Should().Be(1);

                // Audit: one DocumentRepost per document (no duplicates from no-op callers).
                var repostAuditCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                    "SELECT COUNT(*) FROM platform_audit_events WHERE entity_id = @Id AND action_code = @Action",
                    new { Id = docId, Action = AuditActionCodes.DocumentRepost },
                    cancellationToken: CancellationToken.None));
                repostAuditCount.Should().Be(1);
            }
        }
    }

    private IHost CreateHost(ActorIdentity? actor, ItState? state = null)
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                // Composed pipeline needs document definition that has an Operational Register posting handler.
                services.AddSingleton<IDefinitionsContributor, ItDocContributor>();

                // Handler state (register code + amount).
                services.AddSingleton(state ?? new ItState());
                // IMPORTANT: Definitions resolver requests the concrete handler type.
                services.AddScoped<ItOpregPostingHandler>();

                // Actor is optional. Tests can't use Runtime's internal NullCurrentActorContext.
                services.AddScoped<ICurrentActorContext>(_ => actor is null
                    ? new ItNullCurrentActorContext()
                    : new ItFixedCurrentActorContext(actor));
            });

    private static async Task<Guid> UpsertRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<NGB.Runtime.OperationalRegisters.IOperationalRegisterManagementService>();
        return await mgmt.UpsertAsync(code, name: code.ToUpperInvariant(), CancellationToken.None);
    }

    private static async Task ReplaceRegisterResourcesAsync(
        IHost host,
        Guid registerId,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<NGB.Runtime.OperationalRegisters.IOperationalRegisterManagementService>();
        await mgmt.ReplaceResourcesAsync(registerId, resources, CancellationToken.None);
    }

    private static async Task ReplaceRegisterDimensionRulesAsync(
        IHost host,
        Guid registerId,
        IReadOnlyList<OperationalRegisterDimensionRule> newRules)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<NGB.Runtime.OperationalRegisters.IOperationalRegisterManagementService>();
        await mgmt.ReplaceDimensionRulesAsync(registerId, newRules, CancellationToken.None);
    }

    private static async Task EnsureMovementsSchemaAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
        await store.EnsureSchemaAsync(registerId, CancellationToken.None);
    }

    private static async Task EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var existing = await repo.GetForAdminAsync(includeDeleted: true, ct: CancellationToken.None);
        static bool HasNotDeleted(IReadOnlyList<ChartOfAccountsAdminItem> items, string code) =>
            items.Any(x => !x.IsDeleted && string.Equals(x.Account.Code, code, StringComparison.OrdinalIgnoreCase));

        if (!HasNotDeleted(existing, "50"))
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        if (!HasNotDeleted(existing, "90.1"))
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "90.1",
                Name: "Revenue",
                AccountType.Income,
                StatementSection: StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }
    }

    private static async Task PostOneEntryAsync(
        IAccountingPostingContext ctx,
        Guid documentId,
        DateTime periodUtc,
        decimal amount,
        CancellationToken ct)
    {
        var chart = await ctx.GetChartOfAccountsAsync(ct);
        var cash = chart.Get("50");
        var revenue = chart.Get("90.1");
        ctx.Post(documentId, periodUtc, debit: cash, credit: revenue, amount);
    }

    private sealed class ItState
    {
        public string RegisterCode { get; set; } = string.Empty;
        public decimal Amount { get; set; } = 1m;
    }

    private sealed class ItDocContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(DocType, b => b
                .Metadata(new DocumentTypeMetadata(
                    DocType,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Composed Concurrency"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .OperationalRegisterPostingHandler<ItOpregPostingHandler>());
        }
    }

    private sealed class ItOpregPostingHandler(ItState state) : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => DocType;

        public Task BuildMovementsAsync(NGB.Core.Documents.DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct)
        {
            // Single movement per document. No dimensions (Guid.Empty set) => rules can be empty.
            builder.Add(
                state.RegisterCode,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: document.DateUtc,
                    DimensionSetId: Guid.Empty,
                    Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["amount"] = state.Amount
                    }));

            return Task.CompletedTask;
        }
    }

    private sealed class ItNullCurrentActorContext : ICurrentActorContext
    {
        public ActorIdentity? Current => null;
    }

    private sealed class ItFixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
