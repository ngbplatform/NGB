using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

/// <summary>
/// P0: No-op operations must NOT call AuditLogService.
/// Otherwise we get hidden side effects: platform_users.updated_at_utc changes (actor upsert)
/// even though no audit event is written.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditLog_NoOpOperations_DoNotTouchPlatformUsers_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Catalog_MarkDeleted_Twice_WithActor_DoesNotTouchPlatformUsersUpdatedAt()
    {
        var subject = "kc|noop-user-cat-" + Guid.CreateVersion7().ToString("N")[..8];

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "noop.cat@example.com",
                        DisplayName: "NoOp Catalog Actor")));
            });

        Guid catalogId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync("it_cat_audit_noop", manageTransaction: true, ct: CancellationToken.None);

            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None);
        }

        var t1 = await GetUserUpdatedAtUtcAsync(subject);
        t1.Should().NotBeNull("first call must upsert the actor");

        // Ensure a timestamp delta, otherwise two updates could land in the same tick in rare cases.
        await Task.Delay(25);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None); // no-op
        }

        var t2 = await GetUserUpdatedAtUtcAsync(subject);
        t2.Should().Be(t1, "no-op must not touch platform_users (actor upsert)");

        // And no second audit event must exist.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Catalog,
                    EntityId: catalogId,
                    ActionCode: AuditActionCodes.CatalogMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task CoA_SetActive_NoOp_WithActor_DoesNotTouchPlatformUsersUpdatedAt()
    {
        var subject = "kc|noop-user-coa-act-" + Guid.CreateVersion7().ToString("N")[..8];

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "noop.coa.setactive@example.com",
                        DisplayName: "NoOp CoA SetActive Actor")));
            });

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = "it_noop_act_" + Guid.CreateVersion7().ToString("N")[..8];
            accountId = await coa.CreateAsync(
                new CreateAccountRequest(Code: code, Name: "NoOp", Type: AccountType.Asset),
                CancellationToken.None);

            await coa.SetActiveAsync(accountId, isActive: false, CancellationToken.None);
        }

        var t1 = await GetUserUpdatedAtUtcAsync(subject);
        t1.Should().NotBeNull("first SetActive must write audit and upsert actor");

        await Task.Delay(25);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await coa.SetActiveAsync(accountId, isActive: false, CancellationToken.None); // no-op
        }

        var t2 = await GetUserUpdatedAtUtcAsync(subject);
        t2.Should().Be(t1, "no-op SetActive must not touch platform_users");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountSetActive,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task CoA_SoftDelete_NoOp_WithActor_DoesNotTouchPlatformUsersUpdatedAt()
    {
        var subject = "kc|noop-user-coa-del-" + Guid.CreateVersion7().ToString("N")[..8];

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "noop.coa.softdelete@example.com",
                        DisplayName: "NoOp CoA SoftDelete Actor")));
            });

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = "it_noop_del_" + Guid.CreateVersion7().ToString("N")[..8];
            accountId = await coa.CreateAsync(
                new CreateAccountRequest(Code: code, Name: "NoOp", Type: AccountType.Asset),
                CancellationToken.None);

            await coa.MarkForDeletionAsync(accountId, CancellationToken.None);
        }

        var t1 = await GetUserUpdatedAtUtcAsync(subject);
        t1.Should().NotBeNull("first SoftDelete must write audit and upsert actor");

        await Task.Delay(25);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await coa.MarkForDeletionAsync(accountId, CancellationToken.None); // no-op
        }

        var t2 = await GetUserUpdatedAtUtcAsync(subject);
        t2.Should().Be(t1, "no-op SoftDelete must not touch platform_users");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    private async Task<DateTime?> GetUserUpdatedAtUtcAsync(string authSubject)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        return await conn.ExecuteScalarAsync<DateTime?>(
            "SELECT updated_at_utc FROM platform_users WHERE auth_subject = @s;",
            new { s = authSubject });
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
