using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.AuditLog;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_OperationalRegisterManagement_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string RegisterCode = "it_opreg_aud_rb";
    private const string RegisterName = "It OpReg Audit Rollback";

    private const string AuthSubjectUpsert = "kc|audit-opreg-upsert-rollback";
    private const string AuthSubjectRules = "kc|audit-opreg-rules-rollback";
    private const string AuthSubjectResources = "kc|audit-opreg-resources-rollback";

    [Fact]
    public async Task Upsert_Create_WhenAuditWriterThrowsAfterInsert_RollsBack_Register_Audit_And_Actor()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubjectUpsert,
                        Email: "audit.opreg.upsert.rollback@example.com",
                        DisplayName: "Audit OpReg Upsert Rollback")));

                // Override the AuditEvent writer to throw AFTER it wrote the event + changes.
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        var registerId = OperationalRegisterId.FromCode(RegisterCode);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            var act = () => svc.UpsertAsync(RegisterCode, RegisterName, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var regCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM operational_registers WHERE register_id = @id;",
            new { id = registerId });
        regCount.Should().Be(0, "audit failure must rollback the register upsert");

        var eventCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        eventCount.Should().Be(0, "audit rows must not be committed if the transaction rolls back");

        var changeCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        changeCount.Should().Be(0, "audit change rows must not be committed if the transaction rolls back");

        var userCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubjectUpsert });
        userCount.Should().Be(0, "actor upsert must rollback together with audit event");
    }

    [Fact]
    public async Task ReplaceDimensionRules_WhenAuditWriterThrowsAfterInsert_RollsBack_Rules_Dimensions_Audit_And_Actor()
    {
        // Arrange: create a register first (success path)
        Guid registerId;
        await using (var scope = IntegrationHostFactory.Create(Fixture.ConnectionString).Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            registerId = await svc.UpsertAsync(RegisterCode, RegisterName, CancellationToken.None);
        }

        int baselineEvents;
        int baselineChanges;
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            baselineEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
            baselineChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        }

        var dimensionId = Guid.CreateVersion7();
        var rules = new[]
        {
            new OperationalRegisterDimensionRule(
                DimensionId: dimensionId,
                DimensionCode: "it_dim_customer",
                Ordinal: 100,
                IsRequired: false)
        };

        using var failingHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubjectRules,
                        Email: "audit.opreg.rules.rollback@example.com",
                        DisplayName: "Audit OpReg Rules Rollback")));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        // Act
        await using (var scope = failingHost.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            var act = () => svc.ReplaceDimensionRulesAsync(registerId, rules, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert
        await using var assertConn = new NpgsqlConnection(Fixture.ConnectionString);
        await assertConn.OpenAsync();

        var ruleCount = await assertConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM operational_register_dimension_rules WHERE register_id = @id;",
            new { id = registerId });
        ruleCount.Should().Be(0, "audit failure must rollback the rules replace");

        var dimCount = await assertConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_dimensions WHERE dimension_id = @id;",
            new { id = dimensionId });
        dimCount.Should().Be(0, "dimension upsert for rules must rollback together with business change");

        var eventCount = await assertConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        eventCount.Should().Be(baselineEvents, "audit rows must not be committed if the transaction rolls back");

        var changeCount = await assertConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        changeCount.Should().Be(baselineChanges, "audit change rows must not be committed if the transaction rolls back");

        var userCount = await assertConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubjectRules });
        userCount.Should().Be(0, "actor upsert must rollback together with audit event");
    }

    [Fact]
    public async Task ReplaceResources_WhenAuditWriterThrowsAfterInsert_RollsBack_Resources_Audit_And_Actor()
    {
        // Arrange: create a register first (success path)
        Guid registerId;
        await using (var scope = IntegrationHostFactory.Create(Fixture.ConnectionString).Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            registerId = await svc.UpsertAsync(RegisterCode, RegisterName, CancellationToken.None);
        }

        int baselineEvents;
        int baselineChanges;
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            baselineEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
            baselineChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        }

        var resources = new[]
        {
            new OperationalRegisterResourceDefinition(
                Code: "amount",
                Name: "Amount",
                Ordinal: 100)
        };

        using var failingHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubjectResources,
                        Email: "audit.opreg.resources.rollback@example.com",
                        DisplayName: "Audit OpReg Resources Rollback")));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        // Act
        await using (var scope = failingHost.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            var act = () => svc.ReplaceResourcesAsync(registerId, resources, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert
        await using var assertConn = new NpgsqlConnection(Fixture.ConnectionString);
        await assertConn.OpenAsync();

        var resCount = await assertConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM operational_register_resources WHERE register_id = @id;",
            new { id = registerId });
        resCount.Should().Be(0, "audit failure must rollback the resources replace");

        var eventCount = await assertConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        eventCount.Should().Be(baselineEvents, "audit rows must not be committed if the transaction rolls back");

        var changeCount = await assertConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        changeCount.Should().Be(baselineChanges, "audit change rows must not be committed if the transaction rolls back");

        var userCount = await assertConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubjectResources });
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
