using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.UnitOfWork;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_NoOp_WithActor_DoesNotTouchAuditOrUsers_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|doc-draft-noop-actor-p0";
    private static readonly DateTime DateUtc = new(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpdateDraftAsync_WhenCallerPassesNoFields_IsExplicitNoOp_NoAudit_NoActorUpsert()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);

        var baseline = await CaptureBaselineAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            var didWork = await drafts.UpdateDraftAsync(
                documentId: Guid.CreateVersion7(),
                number: null,
                dateUtc: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            didWork.Should().BeFalse("explicit no-op must return false");
        }

        await AssertNoAuditOrUserSideEffectsAsync(baseline);
    }

    [Fact]
    public async Task UpdateDraftAsync_WhenNoChanges_IsNoOp_NoAudit_NoActorUpsert()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);

        // Arrange: create a Draft row WITHOUT calling any service that writes audit.
        var documentId = Guid.CreateVersion7();
        await CreateDraftRowWithoutAuditAsync(host, documentId, typeCode: "it_doc_noop_draft", number: "D-0001", dateUtc: DateUtc);

        var baseline = await CaptureBaselineAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            var didWork = await drafts.UpdateDraftAsync(
                documentId: documentId,
                number: "D-0001",
                dateUtc: DateUtc,
                manageTransaction: true,
                ct: CancellationToken.None);

            didWork.Should().BeFalse("no-op update must return false");
        }

        await AssertNoAuditOrUserSideEffectsAsync(baseline);
    }

    [Fact]
    public async Task DeleteDraftAsync_WhenDocumentDoesNotExist_IsIdempotentNoOp_NoAudit_NoActorUpsert()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);

        var baseline = await CaptureBaselineAsync();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            var didWork = await drafts.DeleteDraftAsync(
                documentId: Guid.CreateVersion7(),
                manageTransaction: true,
                ct: CancellationToken.None);

            didWork.Should().BeFalse("delete of a missing draft must be an idempotent no-op");
        }

        await AssertNoAuditOrUserSideEffectsAsync(baseline);
    }

    private static IHost CreateHostWithActor(string cs)
    {
        return IntegrationHostFactory.Create(cs, services =>
        {
            services.AddScoped<ICurrentActorContext>(_ =>
                new FixedCurrentActorContext(new ActorIdentity(
                    AuthSubject: AuthSubject,
                    Email: "doc.draft.noop.actor@example.com",
                    DisplayName: "Doc Draft NoOp Actor")));
        });
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed record Baseline(int AuditEvents, int AuditChanges, int UsersForSubject);

    private async Task<Baseline> CaptureBaselineAsync()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var eventsCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var changesCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        var usersCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject });

        return new Baseline(eventsCount, changesCount, usersCount);
    }

    private async Task AssertNoAuditOrUserSideEffectsAsync(Baseline baseline)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;"))
            .Should().Be(baseline.AuditEvents);

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;"))
            .Should().Be(baseline.AuditChanges);

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject }))
            .Should().Be(baseline.UsersForSubject,
                "no-op operations must not trigger actor upsert via AuditLogService");
    }

    private static async Task CreateDraftRowWithoutAuditAsync(
        IHost host,
        Guid documentId,
        string typeCode,
        string? number,
        DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var now = new DateTime(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc);
            await repo.CreateAsync(new DocumentRecord
            {
                Id = documentId,
                TypeCode = typeCode,
                Number = number,
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }
}
