using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_ChartOfAccountsLifecycle_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAccount_WritesAuditEvent_AndUpsertsActor()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-user-1",
                        Email: "coa.user@example.com",
                        DisplayName: "CoA User")));
            });

        Guid accountId;
        string code;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            code = "AUD-" + Guid.CreateVersion7().ToString("N")[..8];
            accountId = await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: "Cash",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Forbid,
                    IsActive: true,
                    DimensionRules: null),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountCreate,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.ActorUserId.Should().NotBeNull();

            var user = await users.GetByAuthSubjectAsync("kc|coa-user-1", CancellationToken.None);
            user.Should().NotBeNull();
            user!.UserId.Should().Be(ev.ActorUserId!.Value);

            ev.Changes.Select(c => c.FieldPath)
                .Should()
                .Contain(["code", "name", "account_type", "statement_section", "negative_balance_policy", "is_active"
                ]);

            ev.Changes.Single(c => c.FieldPath == "code").NewValueJson.Should().Contain(code);
            ev.Changes.Single(c => c.FieldPath == "name").NewValueJson.Should().Contain("Cash");
            ev.Changes.Single(c => c.FieldPath == "negative_balance_policy").NewValueJson.Should().Contain("forbid");
            ev.Changes.Single(c => c.FieldPath == "is_active").NewValueJson.Should().Contain("true");
        }
    }

    [Fact]
    public async Task Update_NoOp_DoesNotWriteSecondAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-user-2",
                        Email: null,
                        DisplayName: null)));
            });

        Guid accountId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = "AUD-" + Guid.CreateVersion7().ToString("N")[..8];
            accountId = await svc.CreateAsync(
                new CreateAccountRequest(Code: code, Name: "Bank", Type: AccountType.Asset),
                CancellationToken.None);

            await svc.UpdateAsync(
                new UpdateAccountRequest(AccountId: accountId, Name: "Bank Updated"),
                CancellationToken.None);

            await svc.UpdateAsync(
                new UpdateAccountRequest(AccountId: accountId, Name: "Bank Updated"),
                CancellationToken.None); // strict no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountUpdate,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);

            var ev = events.Single();
            ev.Changes.Should().ContainSingle(c => c.FieldPath == "name");
            ev.Changes.Single(c => c.FieldPath == "name").OldValueJson.Should().Contain("Bank");
            ev.Changes.Single(c => c.FieldPath == "name").NewValueJson.Should().Contain("Bank Updated");
        }
    }

    [Fact]
    public async Task SetActive_And_SoftDelete_AreIdempotent_AndAuditedOnceEach()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-user-3",
                        Email: null,
                        DisplayName: "CoA Admin")));
            });

        Guid accountId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = "AUD-" + Guid.CreateVersion7().ToString("N")[..8];
            accountId = await svc.CreateAsync(
                new CreateAccountRequest(Code: code, Name: "Temp", Type: AccountType.Asset),
                CancellationToken.None);

            await svc.SetActiveAsync(accountId, isActive: false, CancellationToken.None);
            await svc.SetActiveAsync(accountId, isActive: false, CancellationToken.None); // no-op

            await svc.MarkForDeletionAsync(accountId, CancellationToken.None);
            await svc.MarkForDeletionAsync(accountId, CancellationToken.None); // no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var setActiveEvents = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountSetActive,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            setActiveEvents.Should().HaveCount(1);
            setActiveEvents.Single().Changes.Single(c => c.FieldPath == "is_active").NewValueJson.Should().Contain("false");

            var softDeleteEvents = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            softDeleteEvents.Should().HaveCount(1);
            softDeleteEvents.Single().Changes.Single(c => c.FieldPath == "is_deleted").NewValueJson.Should().Contain("true");
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
