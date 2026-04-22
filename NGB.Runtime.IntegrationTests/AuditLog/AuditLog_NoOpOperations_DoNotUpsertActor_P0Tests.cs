using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Definitions;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

/// <summary>
/// Guardrail: strict no-op operations must not call AuditLogService at all.
/// Otherwise, we silently upsert/touch platform_users (updated_at_utc changes) even when no audit event is written.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditLog_NoOpOperations_DoNotUpsertActor_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Catalog_MarkDeleted_NoOp_DoesNotTouchPlatformUsers()
    {
        const string subject = "kc|noop-actor-catalog";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestCatalogContributor>();
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "noop.catalog@example.com",
                        DisplayName: "NoOp Catalog Actor")));
            });

        Guid catalogId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            catalogId = await drafts.CreateAsync("it_cat_audit_noop", manageTransaction: true, ct: CancellationToken.None);

            // First delete => writes audit and upserts actor
            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None);
        }

        var baseline = await GetUserRowAsync(subject);

        // Second delete => strict no-op; must NOT touch platform_users.updated_at_utc
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<ICatalogDraftService>();
            await drafts.MarkForDeletionAsync(catalogId, manageTransaction: true, ct: CancellationToken.None);
        }

        var after = await GetUserRowAsync(subject);
        after.UserId.Should().Be(baseline.UserId);
        after.UpdatedAtUtc.Should().Be(baseline.UpdatedAtUtc, "no-op must not call audit, so actor upsert must not run");
    }

    [Fact]
    public async Task CoA_SetActive_NoOp_DoesNotTouchPlatformUsers()
    {
        const string subject = "kc|noop-actor-coa-active";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "noop.coa@example.com",
                        DisplayName: "NoOp CoA Actor")));
            });

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = $"it_noop_{Guid.CreateVersion7():N}"[..16];
            accountId = await mgmt.CreateAsync(new CreateAccountRequest(
                Code: code,
                Name: "IT NoOp Actor CoA",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsActive: true),
                CancellationToken.None);

            // First set => writes audit and upserts actor
            await mgmt.SetActiveAsync(accountId, isActive: false, CancellationToken.None);
        }

        var baseline = await GetUserRowAsync(subject);

        // Second set to same value => strict no-op; must NOT touch platform_users.updated_at_utc
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await mgmt.SetActiveAsync(accountId, isActive: false, CancellationToken.None);
        }

        var after = await GetUserRowAsync(subject);
        after.UserId.Should().Be(baseline.UserId);
        after.UpdatedAtUtc.Should().Be(baseline.UpdatedAtUtc, "no-op must not call audit, so actor upsert must not run");
    }

    [Fact]
    public async Task CoA_SoftDelete_NoOp_DoesNotTouchPlatformUsers()
    {
        const string subject = "kc|noop-actor-coa-delete";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "noop.coa.delete@example.com",
                        DisplayName: "NoOp CoA Delete Actor")));
            });

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = $"it_noop_{Guid.CreateVersion7():N}"[..16];
            accountId = await mgmt.CreateAsync(new CreateAccountRequest(
                Code: code,
                Name: "IT NoOp Actor CoA Delete",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsActive: true),
                CancellationToken.None);

            // First delete => writes audit and upserts actor
            await mgmt.MarkForDeletionAsync(accountId, CancellationToken.None);
        }

        var baseline = await GetUserRowAsync(subject);

        // Second delete => strict no-op; must NOT touch platform_users.updated_at_utc
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            await mgmt.MarkForDeletionAsync(accountId, CancellationToken.None);
        }

        var after = await GetUserRowAsync(subject);
        after.UserId.Should().Be(baseline.UserId);
        after.UpdatedAtUtc.Should().Be(baseline.UpdatedAtUtc, "no-op must not call audit, so actor upsert must not run");
    }

    private sealed record UserRow(Guid UserId, DateTime UpdatedAtUtc);

    private async Task<UserRow> GetUserRowAsync(string authSubject)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        const string sql = """
                           SELECT
                               user_id AS UserId,
                               updated_at_utc AS UpdatedAtUtc
                           FROM platform_users
                           WHERE auth_subject = @AuthSubject;
                           """;

        return await conn.QuerySingleAsync<UserRow>(sql, new { AuthSubject = authSubject });
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
