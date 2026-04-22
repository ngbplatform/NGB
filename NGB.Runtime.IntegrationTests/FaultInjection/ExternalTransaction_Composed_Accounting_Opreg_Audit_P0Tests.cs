using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Core.AuditLog;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Accounts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Posting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.FaultInjection;

/// <summary>
/// P0: External transaction mode must compose correctly across platform subsystems.
///
/// We intentionally run these operations in a single outer transaction:
/// - PostingEngine (accounting register + posting_log)
/// - OperationalRegisterMovementsApplier (movements + write_log + dirty month)
/// - AuditLogService (audit events + actor upsert)
///
/// Then we validate:
/// - outer rollback removes ALL side effects
/// - outer commit persists ALL side effects
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExternalTransaction_Composed_Accounting_Opreg_Audit_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DocumentTypeCode = "it_doc_ts_ext";

    [Fact]
    public async Task ExternalTransaction_Rollback_RemovesAccounting_Opreg_Audit_And_Actor()
    {
        // Arrange: create register + tables WITHOUT actor (so the test actor can be validated independently).
        using var hostNoActor = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await EnsureMinimalAccountsAsync(hostNoActor);

        var registerCode = UniqueRegisterCode();
        var registerId = await CreateRegisterWithResourcesAndSchemaAsync(hostNoActor, registerCode);
        var movementsTable = await GetMovementsTableAsync(hostNoActor, registerId);

        var actor = NewActor();
        using var host = CreateHostWithActor(actor);

        var documentId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        // Act: run composed work inside an external transaction and then rollback.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();
            var opreg = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await InsertDraftDocumentAsync(uow, documentId, dateUtc: occurredAtUtc, nowUtc: occurredAtUtc, typeCode: DocumentTypeCode, CancellationToken.None);

            var postResult = await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var cash = chart.Get("50");
                    var revenue = chart.Get("90.1");
                    ctx.Post(documentId, period, cash, revenue, 10m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            postResult.Should().Be(PostingResult.Executed);

            var writeResult = await opreg.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                [new OperationalRegisterMovement(documentId, occurredAtUtc, Guid.Empty, new Dictionary<string, decimal> { ["amount"] = 5m })],
                affectedPeriods: null,
                manageTransaction: false,
                ct: CancellationToken.None);

            writeResult.Should().Be(OperationalRegisterWriteResult.Executed);

            await audit.WriteAsync(
                AuditEntityKind.Document,
                documentId,
                AuditActionCodes.DocumentPost,
                changes: [new AuditFieldChange("status", "\"Draft\"", "\"Posted\"")],
                metadata: new { it = "external_tx" },
                correlationId: Guid.CreateVersion7(),
                ct: CancellationToken.None);

            await uow.RollbackAsync(CancellationToken.None);
        }

        // Assert: rollback removed all side effects, including actor upsert.
        await AssertNoSideEffectsAsync(documentId, registerId, movementsTable, actor.AuthSubject);
    }

    [Fact]
    public async Task ExternalTransaction_Commit_PersistsAccounting_Opreg_Audit_And_Actor()
    {
        using var hostNoActor = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await EnsureMinimalAccountsAsync(hostNoActor);

        var registerCode = UniqueRegisterCode();
        var registerId = await CreateRegisterWithResourcesAndSchemaAsync(hostNoActor, registerCode);
        var movementsTable = await GetMovementsTableAsync(hostNoActor, registerId);

        var actor = NewActor();
        using var host = CreateHostWithActor(actor);

        var documentId = Guid.CreateVersion7();
        var occurredAtUtc = new DateTime(2026, 1, 16, 12, 0, 0, DateTimeKind.Utc);
        var period = new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc);
        var month = new DateOnly(occurredAtUtc.Year, occurredAtUtc.Month, 1);

        // Act: commit
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();
            var opreg = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await InsertDraftDocumentAsync(uow, documentId, dateUtc: occurredAtUtc, nowUtc: occurredAtUtc, typeCode: DocumentTypeCode, CancellationToken.None);

            var postResult = await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    var cash = chart.Get("50");
                    var revenue = chart.Get("90.1");
                    ctx.Post(documentId, period, cash, revenue, 10m);
                },
                manageTransaction: false,
                ct: CancellationToken.None);

            postResult.Should().Be(PostingResult.Executed);

            var writeResult = await opreg.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                [new OperationalRegisterMovement(documentId, occurredAtUtc, Guid.Empty, new Dictionary<string, decimal> { ["amount"] = 5m })],
                affectedPeriods: null,
                manageTransaction: false,
                ct: CancellationToken.None);

            writeResult.Should().Be(OperationalRegisterWriteResult.Executed);

            await audit.WriteAsync(
                AuditEntityKind.Document,
                documentId,
                AuditActionCodes.DocumentPost,
                changes: [new AuditFieldChange("status", "\"Draft\"", "\"Posted\"")],
                metadata: new { it = "external_tx" },
                correlationId: Guid.CreateVersion7(),
                ct: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert: commit persisted everything.
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var docCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM documents WHERE id = @d;",
            new { d = documentId });

        docCount.Should().Be(1);

        var accCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d;",
            new { d = documentId });

        accCount.Should().Be(1);

        var completedAt = await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT completed_at_utc FROM accounting_posting_state WHERE document_id = @d AND operation = @op;",
            new { d = documentId, op = (short)PostingOperation.Post });

        completedAt.Should().NotBeNull("posting_log row must be completed when outer transaction commits");

        var opregLogCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_write_state WHERE register_id = @r AND document_id = @d AND operation = @op;",
            new { r = registerId, d = documentId, op = (short)OperationalRegisterWriteOperation.Post });

        opregLogCount.Should().Be(1);

        var dirtyStatus = await conn.ExecuteScalarAsync<short?>(
            "SELECT status FROM operational_register_finalizations WHERE register_id = @r AND period = @p;",
            new { r = registerId, p = month });

        dirtyStatus.Should().Be((short)OperationalRegisterFinalizationStatus.Dirty);

        var movementCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*)::int FROM {movementsTable} WHERE document_id = @d AND is_storno = FALSE;",
            new { d = documentId });

        movementCount.Should().Be(1);

        var auditCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM platform_audit_events WHERE entity_id = @d AND action_code = @a;",
            new { d = documentId, a = AuditActionCodes.DocumentPost });

        auditCount.Should().Be(1);

        var changeCount = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)::int
            FROM platform_audit_event_changes c
            JOIN platform_audit_events e ON e.audit_event_id = c.audit_event_id
            WHERE e.entity_id = @d;
            """,
            new { d = documentId });

        changeCount.Should().Be(1);

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM platform_users WHERE auth_subject = @s;",
            new { s = actor.AuthSubject });

        userCount.Should().Be(1);
    }

    private static string UniqueRegisterCode() => "rr_ext_" + Guid.CreateVersion7().ToString("N")[..8];

    private IHost CreateHostWithActor(ActorIdentity actor)
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => { services.AddScoped<ICurrentActorContext>(_ => new FixedCurrentActorContext(actor)); });

    private static ActorIdentity NewActor()
    {
        var s = "kc|it|external_tx|" + Guid.CreateVersion7().ToString("N");
        return new ActorIdentity(AuthSubject: s, Email: "it@example.test", DisplayName: "Integration Test", IsActive: true);
    }

    private static async Task InsertDraftDocumentAsync(
        IUnitOfWork uow,
        Guid documentId,
        DateTime dateUtc,
        DateTime nowUtc,
        string typeCode,
        CancellationToken ct)
    {
        uow.EnsureActiveTransaction();

        var sql = """
INSERT INTO documents (
    id,
    type_code,
    number,
    date_utc,
    status,
    posted_at_utc,
    marked_for_deletion_at_utc,
    created_at_utc,
    updated_at_utc
)
VALUES (
    @documentId,
    @typeCode,
    NULL,
    @dateUtc,
    1,
    NULL,
    NULL,
    @nowUtc,
    @nowUtc
);
""";

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { documentId, typeCode, dateUtc, nowUtc },
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private static async Task<Guid> CreateRegisterWithResourcesAndSchemaAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var store = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsStore>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var registerId = await mgmt.UpsertAsync(code, name: "IT External Tx Register", CancellationToken.None);

        await mgmt.ReplaceResourcesAsync(registerId,
            [new OperationalRegisterResourceDefinition("amount", "Amount", 1)],
            CancellationToken.None);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        return registerId;
    }

    private static async Task<string> GetMovementsTableAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();
        return OperationalRegisterNaming.MovementsTable(reg!.TableCode);
    }

    private async Task AssertNoSideEffectsAsync(Guid documentId, Guid registerId, string movementsTable, string authSubject)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var docCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM documents WHERE id = @d;",
            new { d = documentId });
        var accCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @d;",
            new { d = documentId });

        var logCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM accounting_posting_state WHERE document_id = @d AND operation = @op;",
            new { d = documentId, op = (short)PostingOperation.Post });

        var movementCount = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*)::int FROM {movementsTable} WHERE document_id = @d;",
            new { d = documentId });

        var opregLogCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_write_state WHERE register_id = @r AND document_id = @d;",
            new { r = registerId, d = documentId });

        var dirtyCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_finalizations WHERE register_id = @r;",
            new { r = registerId });

        var auditCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM platform_audit_events WHERE entity_id = @d;",
            new { d = documentId });

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM platform_users WHERE auth_subject = @s;",
            new { s = authSubject });

        docCount.Should().Be(0);
        accCount.Should().Be(0);
        logCount.Should().Be(0);
        movementCount.Should().Be(0);
        opregLogCount.Should().Be(0);
        dirtyCount.Should().Be(0);
        auditCount.Should().Be(0);
        userCount.Should().Be(0);
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current { get; } = actor;
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
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }

        if (!HasNotDeleted(existing, "90.1"))
        {
            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "90.1",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow
            ), CancellationToken.None);
        }
    }
}
