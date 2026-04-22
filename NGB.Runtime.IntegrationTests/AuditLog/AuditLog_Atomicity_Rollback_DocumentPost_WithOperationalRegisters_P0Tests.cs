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
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

/// <summary>
/// P0: Cross-module atomicity contract for Document.PostAsync when multiple subsystems participate:
/// - Accounting (register + posting_log)
/// - Operational Registers (movements + write_log + finalizations)
/// - AuditLog (events + changes + actor upsert)
///
/// If ANY participant fails, the whole operation must rollback with NO partial writes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_DocumentPost_WithOperationalRegisters_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|audit-opreg-post-rollback-test";
    private static readonly ActorIdentity TestActor = new(
        AuthSubject: AuthSubject,
        Email: "audit.opreg.rollback@example.com",
        DisplayName: "Audit OpReg Post Rollback");

    private static readonly DateTime NowUtc = new(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid BuildingsDimensionId = DeterministicGuid.Create("Dimension|buildings");
    private static readonly Guid BuildingsValueId = DeterministicGuid.Create("DimensionValue|buildings|b1");

    [Fact]
    public async Task PostAsync_WhenOpregWriteFails_IsAtomic_NoAccounting_NoPostingLog_NoAudit_NoActor_NoOpregLogs()
    {
        // Arrange: opreg handler produces a non-empty DimensionSetId, but the register has NO dimension rules.
        // That must fail movement dimension validation.
        using var host = CreateHost(throwAfterAuditWrite: false);

        await SeedMinimalCoaWithoutAuditAsync(host);

        var code = UniqueRegisterCode();
        var registerId = await CreateRegisterAsync(host, code);

        // Resources only. No dimension rules => non-empty DimensionSetId must fail.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            await mgmt.ReplaceResourcesAsync(registerId,
                [new OperationalRegisterResourceDefinition("amount", "Amount", 1)],
                CancellationToken.None);
        }

        await EnsurePlatformDimensionExistsAsync(host, BuildingsDimensionId);
        await ConfigureHandlerStateAsync(host, code, amount: 10m);

        var docId = await CreateDraftWithoutAuditAsync(host, typeCode: "it_doc_opreg", number: "IT-AUD-OPREG-1", dateUtc: NowUtc);

        var baseline = await CaptureAuditBaselineAsync();

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.PostAsync(docId, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: docId,
                    period: NowUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 1m);
            }, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
            ex.Which.Message.Should().Contain("dimension not allowed");
        }

        // Assert: absolutely no persisted side effects.
        await AssertNoPostSideEffectsAsync(docId, registerId, baseline);
    }

    [Fact]
    public async Task PostAsync_WhenAuditWriterThrowsAfterWrite_RollsBack_Accounting_Opreg_PostingLog_Audit_And_Actor()
    {
        // Arrange: opreg succeeds, but audit writer throws AFTER it wrote audit rows.
        using var host = CreateHost(throwAfterAuditWrite: true);

        await SeedMinimalCoaWithoutAuditAsync(host);

        var code = UniqueRegisterCode();
        var registerId = await CreateRegisterAsync(host, code);

        await ConfigureRegisterAsync(host, registerId, BuildingsDimensionId, dimensionCode: "Buildings");
        await EnsurePlatformDimensionExistsAsync(host, BuildingsDimensionId);
        await ConfigureHandlerStateAsync(host, code, amount: 10m);

        var docId = await CreateDraftWithoutAuditAsync(host, typeCode: "it_doc_opreg", number: "IT-AUD-OPREG-2", dateUtc: NowUtc);

        var baseline = await CaptureAuditBaselineAsync();
        baseline.UsersForSubject.Should().Be(0, "setup must not create the actor row; actor upsert must be tested inside Post transaction");

        // Enable actor only for the posting scope.
        host.Services.GetRequiredService<ItOpregTestState>().AuditActorEnabled = true;

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.PostAsync(docId, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: docId,
                    period: NowUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 123m);
            }, CancellationToken.None);

            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert: audit failure must rollback BOTH accounting and opreg.
        await AssertNoPostSideEffectsAsync(docId, registerId, baseline);
    }

    private IHost CreateHost(bool throwAfterAuditWrite)
    {
        return IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItOpregTestDocumentContributor>();
                services.AddSingleton<ItOpregTestState>();
                services.AddScoped<ItOpregTestOperationalRegisterPostingHandler>();

                // Enable audit with a deterministic actor so we can assert actor upsert rollback.
                services.AddScoped<ICurrentActorContext, ConditionalCurrentActorContext>();

                if (throwAfterAuditWrite)
                {
                    services.AddScoped<PostgresAuditEventWriter>();
                    services.AddScoped<IAuditEventWriter>(sp =>
                        new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
                }
            });
    }

    private static string UniqueRegisterCode() => "rr_" + Guid.CreateVersion7().ToString("N")[..8];

    private static async Task SeedMinimalCoaWithoutAuditAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var cashId = Guid.CreateVersion7();
            var revId = Guid.CreateVersion7();

            await uow.Connection.ExecuteAsync(
                """
                INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
                VALUES (@id, @code, @name, @type, @section, @neg);
                """,
                new
                {
                    id = cashId,
                    code = "50",
                    name = "Cash",
                    type = (short)AccountType.Asset,
                    section = (short)StatementSection.Assets,
                    neg = (short)NegativeBalancePolicy.Allow
                },
                transaction: uow.Transaction);

            await uow.Connection.ExecuteAsync(
                """
                INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
                VALUES (@id, @code, @name, @type, @section, @neg);
                """,
                new
                {
                    id = revId,
                    code = "90.1",
                    name = "Revenue",
                    type = (short)AccountType.Income,
                    section = (short)StatementSection.Income,
                    neg = (short)NegativeBalancePolicy.Allow
                },
                transaction: uow.Transaction);
        }, CancellationToken.None);
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

    private static async Task EnsurePlatformDimensionExistsAsync(IHost host, Guid dimensionId)
    {
        // DimensionSetId references platform_dimensions via FK.
        var code = "it_dim_" + dimensionId.ToString("N")[..8];

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        await uow.Connection.ExecuteAsync(
            """
            INSERT INTO platform_dimensions (dimension_id, code, name, is_active, is_deleted)
            VALUES (@Id, @Code, @Name, TRUE, FALSE)
            ON CONFLICT (dimension_id) DO NOTHING;
            """,
            new { Id = dimensionId, Code = code, Name = "IT Dimension " + code },
            transaction: uow.Transaction);
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

    private static async Task<Guid> CreateDraftWithoutAuditAsync(IHost host, string typeCode, string number, DateTime dateUtc)
    {
        // We bypass DocumentDraftService intentionally so that BEFORE the Post attempt
        // platform_audit_events are empty. This lets us assert full rollback precisely.
        var id = Guid.CreateVersion7();
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = id,
                TypeCode = typeCode,
                Number = number,
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return id;
    }

    private sealed record AuditBaseline(int AuditEvents, int AuditChanges, int UsersForSubject);

    private async Task<AuditBaseline> CaptureAuditBaselineAsync()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var events = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var changes = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        var users = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject });

        return new AuditBaseline(events, changes, users);
    }

    private async Task AssertNoPostSideEffectsAsync(Guid documentId, Guid registerId, AuditBaseline baseline)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<short>(
            "SELECT status FROM documents WHERE id = @id;",
            new { id = documentId }))
            .Should().Be((short)DocumentStatus.Draft);

        (await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT posted_at_utc FROM documents WHERE id = @id;",
            new { id = documentId }))
            .Should().BeNull();

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @id;",
            new { id = documentId }))
            .Should().Be(0, "failed Post must not persist accounting register entries");

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id;",
            new { id = documentId }))
            .Should().Be(0, "failed Post must not persist accounting posting_log row");

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM operational_register_write_state WHERE document_id = @id;",
            new { id = documentId }))
            .Should().Be(0, "failed Post must not persist operational register write_log rows");

        var period = new DateOnly(NowUtc.Year, NowUtc.Month, 1);
        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM operational_register_finalizations WHERE register_id = @r AND period = @p;",
            new { r = registerId, p = period }))
            .Should().Be(0, "failed Post must not mark operational register months dirty/finalized");

        // If the per-register movements table exists (it may or may not, depending on when schema ensure runs),
        // it must not contain any rows for this document.
        var tableCode = await conn.ExecuteScalarAsync<string?>(
            "SELECT table_code FROM operational_registers WHERE register_id = @id;",
            new { id = registerId });

        tableCode.Should().NotBeNullOrWhiteSpace();
        var movementsTable = OperationalRegisterNaming.MovementsTable(tableCode!);
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT to_regclass('public.' || @tbl) IS NOT NULL;",
            new { tbl = movementsTable });

        if (exists)
        {
            (await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {movementsTable} WHERE document_id = @id;", new { id = documentId }))
                .Should().Be(0, "failed Post must rollback operational register movements");
        }
        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE entity_id = @id;",
            new { id = documentId }))
            .Should().Be(0, "failed Post must not persist document audit events");

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes c JOIN platform_audit_events e ON e.audit_event_id = c.audit_event_id WHERE e.entity_id = @id;",
            new { id = documentId }))
            .Should().Be(0, "failed Post must not persist document audit change rows");



        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events;"))
            .Should().Be(baseline.AuditEvents, "failed Post must rollback audit events");

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes;"))
            .Should().Be(baseline.AuditChanges, "failed Post must rollback audit change rows");

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject }))
            .Should().Be(baseline.UsersForSubject, "actor upsert must rollback together with audit event");
    }
    private sealed class ConditionalCurrentActorContext(ItOpregTestState state) : ICurrentActorContext
    {
        public ActorIdentity? Current => state.AuditActorEnabled ? TestActor : null;
    }

private sealed class ThrowAfterWriteAuditEventWriter(IAuditEventWriter inner) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);

            // Only simulate failure for document posting audit to keep test setup (register admin) functional.
            if (auditEvent.ActionCode == "document.post")
                throw new NgbInvariantViolationException("simulated audit failure");
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            if (auditEvents is null)
                throw new ArgumentNullException(nameof(auditEvents));

            for (var i = 0; i < auditEvents.Count; i++)
                await WriteAsync(auditEvents[i], ct);
        }
    }
private sealed class ItOpregTestState
    {
        public bool AuditActorEnabled { get; set; }
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
                throw new NgbInvariantViolationException("Test state RegisterCode is not configured.");

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
