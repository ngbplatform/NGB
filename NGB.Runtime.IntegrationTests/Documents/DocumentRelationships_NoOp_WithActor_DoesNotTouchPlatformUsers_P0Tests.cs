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
public sealed class DocumentRelationships_NoOp_WithActor_DoesNotTouchPlatformUsers_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|docrel-noop-actor-p0";

    [Fact]
    public async Task CreateAndDelete_NoOp_WithActor_DoesNotTouchAuditOrUsers()
    {
        using var host = CreateHostWithActor(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var (fromId, toId) = await CreateTwoDraftDocsAsync(scope.ServiceProvider);
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();

        const string code = "based_on";

        // Act 1: create (audited)
        (await svc.CreateAsync(fromId, toId, code, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        var baselineAfterCreate = await CaptureBaselineAsync();

        // Act 2: create again (no-op)
        (await svc.CreateAsync(fromId, toId, code, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeFalse();

        await AssertBaselineUnchangedAsync(baselineAfterCreate, because: "idempotent no-op Create must not write audit or touch platform_users");

        // Act 3: delete (audited)
        (await svc.DeleteAsync(fromId, toId, code, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeTrue();

        var baselineAfterDelete = await CaptureBaselineAsync();

        // Act 4: delete again (no-op)
        (await svc.DeleteAsync(fromId, toId, code, manageTransaction: true, ct: CancellationToken.None))
            .Should().BeFalse();

        await AssertBaselineUnchangedAsync(baselineAfterDelete, because: "idempotent no-op Delete must not write audit or touch platform_users");
    }

    private static IHost CreateHostWithActor(string cs)
        => IntegrationHostFactory.Create(cs, services =>
        {
            services.AddScoped<ICurrentActorContext>(_ => new FixedCurrentActorContext(new ActorIdentity(
                AuthSubject: AuthSubject,
                Email: "docrel.noop.actor@example.com",
                DisplayName: "DocRel NoOp Actor")));
        });

    private sealed record Baseline(int AuditEvents, int AuditChanges, int UsersForSubject, DateTime? UserUpdatedAtUtc);

    private async Task<Baseline> CaptureBaselineAsync()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var auditEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var auditChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        var usersForSubject = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject });

        DateTime? updatedAt = null;
        if (usersForSubject > 0)
        {
            updatedAt = await conn.ExecuteScalarAsync<DateTime>(
                "SELECT updated_at_utc FROM platform_users WHERE auth_subject = @s;",
                new { s = AuthSubject });
        }

        return new Baseline(auditEvents, auditChanges, usersForSubject, updatedAt);
    }

    private async Task AssertBaselineUnchangedAsync(Baseline baseline, string because)
    {
        var now = await CaptureBaselineAsync();

        now.AuditEvents.Should().Be(baseline.AuditEvents, because);
        now.AuditChanges.Should().Be(baseline.AuditChanges, because);
        now.UsersForSubject.Should().Be(baseline.UsersForSubject, because);

        if (baseline.UsersForSubject == 0)
        {
            now.UserUpdatedAtUtc.Should().BeNull(because);
        }
        else
        {
            now.UserUpdatedAtUtc.Should().Be(baseline.UserUpdatedAtUtc, because);
        }
    }

    private static async Task<(Guid FromId, Guid ToId)> CreateTwoDraftDocsAsync(IServiceProvider sp)
    {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var repo = sp.GetRequiredService<IDocumentRepository>();

        var fromId = Guid.CreateVersion7();
        var toId = Guid.CreateVersion7();
        var nowUtc = DateTime.UtcNow;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = fromId,
                TypeCode = "it_alpha",
                Number = "A-0001",
                DateUtc = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await repo.CreateAsync(new DocumentRecord
            {
                Id = toId,
                TypeCode = "it_beta",
                Number = "B-0001",
                DateUtc = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return (fromId, toId);
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
