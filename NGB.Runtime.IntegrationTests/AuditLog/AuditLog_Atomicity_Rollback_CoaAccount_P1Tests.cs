using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_CoaAccount_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|audit-coa-rollback-test";

    [Fact]
    public async Task CreateAccount_WhenAuditWriterThrowsAfterInsert_RollsBack_Account_DimensionRules_Dimensions_Audit_And_Actor()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: "audit.coa.rollback@example.com",
                        DisplayName: "Audit CoA Rollback")));

                // Override the AuditEvent writer to throw AFTER it wrote the event + changes.
                // This simulates a failure happening late in the business transaction.
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        var accountCode = "it_acc_aud_rb_" + Guid.CreateVersion7().ToString("N")[..8];
        var dimCode = "it_dim_aud_rb_" + Guid.CreateVersion7().ToString("N")[..8];

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var request = new CreateAccountRequest(
                Code: accountCode,
                Name: "IT Rollback Account",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Warn,
                IsActive: true,
                DimensionRules: new[]
                {
                    new AccountDimensionRuleRequest(DimensionCode: dimCode, IsRequired: true, Ordinal: 10)
                });

            var act = () => coa.CreateAsync(request, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var accountCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting_accounts WHERE code = @code;",
            new { code = accountCode });

        accountCount.Should().Be(0, "audit failure must rollback account creation");

        var rulesCount = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
              FROM accounting_account_dimension_rules r
              JOIN accounting_accounts a ON a.account_id = r.account_id
              WHERE a.code = @code;",
            new { code = accountCode });

        rulesCount.Should().Be(0, "dimension rule rows must rollback with the account");

        var dimensionCount = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*)
              FROM platform_dimensions
              WHERE code_norm = lower(btrim(@c))
                AND is_deleted = FALSE;",
            new { c = dimCode });

        dimensionCount.Should().Be(0, "dimension upsert must rollback with the business transaction");

        var eventCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events;");

        eventCount.Should().Be(0, "audit rows must not be committed if the transaction rolls back");

        var changeCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes;");

        changeCount.Should().Be(0, "audit change rows must not be committed if the transaction rolls back");

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubject });

        userCount.Should().Be(0, "actor upsert must rollback together with audit event");
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed class ThrowAfterWriteAuditEventWriter(IAuditEventWriter inner) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);
            throw new NotSupportedException("simulated audit failure");
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            if (auditEvents is null)
                throw new ArgumentNullException(nameof(auditEvents));

            for (var i = 0; i < auditEvents.Count; i++)
                await WriteAsync(auditEvents[i], ct);
        }
    }
}
