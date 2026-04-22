using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Metadata.Base;
using NGB.Persistence.AuditLog;
using NGB.PostgreSql.AuditLog;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_ReferenceRegisterManagement_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string RegisterCode = "it_refreg_aud_rb";
    private const string RegisterName = "It RefReg Audit Rollback";

    private const string AuthSubjectUpsert = "kc|audit-refreg-upsert-rollback";
    private const string AuthSubjectFields = "kc|audit-refreg-fields-rollback";
    private const string AuthSubjectRules = "kc|audit-refreg-rules-rollback";

    [Fact]
    public async Task Upsert_Create_WhenAuditWriterThrowsAfterInsert_RollsBack_Register_Schema_Audit_And_Actor()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubjectUpsert,
                        Email: "audit.refreg.upsert.rollback@example.com",
                        DisplayName: "Audit RefReg Upsert Rollback")));

                // Override the AuditEvent writer to throw AFTER it wrote the event + changes.
                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            var act = () => svc.UpsertAsync(
                RegisterCode,
                RegisterName,
                ReferenceRegisterPeriodicity.NonPeriodic,
                ReferenceRegisterRecordMode.Independent,
                CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert
        var registerId = ReferenceRegisterId.FromCode(RegisterCode);
        var recordsTable = ReferenceRegisterNaming.RecordsTable(RegisterCode);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var regCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM reference_registers WHERE register_id = @id;",
            new { id = registerId });
        regCount.Should().Be(0, "audit failure must rollback the register upsert");

        // Per-register schema ensure may run outside the business transaction (e.g., separate connection / lock scope).
        // What must be atomic on audit failure is: metadata + audit rows + actor upsert.
        // The physical table may already exist (or be created) even if the upsert transaction rolls back.
        var regclass = await conn.ExecuteScalarAsync<string?>(
            "SELECT to_regclass(@t)::text;",
            new { t = $"public.{recordsTable}" });

        if (regclass is not null)
        {
            var rows = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {recordsTable};");
            rows.Should().Be(0, "no records should be inserted when the transaction rolls back");
        }

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
    public async Task ReplaceFields_WhenAuditWriterThrowsAfterInsert_RollsBack_Fields_Audit_And_Actor()
    {
        // Arrange: create a register first (success path)
        Guid registerId;
        await using (var scope = IntegrationHostFactory.Create(Fixture.ConnectionString).Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await svc.UpsertAsync(
                RegisterCode,
                RegisterName,
                ReferenceRegisterPeriodicity.NonPeriodic,
                ReferenceRegisterRecordMode.Independent,
                CancellationToken.None);
        }

        int baselineEvents;
        int baselineChanges;
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            baselineEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
            baselineChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        }

        var fields = new[]
        {
            new ReferenceRegisterFieldDefinition(
                Code: "amount",
                Name: "Amount",
                Ordinal: 10,
                ColumnType: ColumnType.Decimal,
                IsNullable: false),
            new ReferenceRegisterFieldDefinition(
                Code: "note",
                Name: "Note",
                Ordinal: 20,
                ColumnType: ColumnType.String,
                IsNullable: true),
        };

        using var failingHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubjectFields,
                        Email: "audit.refreg.fields.rollback@example.com",
                        DisplayName: "Audit RefReg Fields Rollback")));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        // Act
        await using (var scope = failingHost.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            var act = () => svc.ReplaceFieldsAsync(registerId, fields, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert
        await using var assertConn = new NpgsqlConnection(Fixture.ConnectionString);
        await assertConn.OpenAsync();

        var fieldCount = await assertConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM reference_register_fields WHERE register_id = @id;",
            new { id = registerId });
        fieldCount.Should().Be(0, "audit failure must rollback the fields replace");

        var eventCount = await assertConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        eventCount.Should().Be(baselineEvents, "audit rows must not be committed if the transaction rolls back");

        var changeCount = await assertConn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        changeCount.Should().Be(baselineChanges, "audit change rows must not be committed if the transaction rolls back");

        var userCount = await assertConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
            new { s = AuthSubjectFields });
        userCount.Should().Be(0, "actor upsert must rollback together with audit event");
    }

    [Fact]
    public async Task ReplaceDimensionRules_WhenAuditWriterThrowsAfterInsert_RollsBack_Rules_Dimensions_Audit_And_Actor()
    {
        // Arrange: create a register first (success path)
        Guid registerId;
        await using (var scope = IntegrationHostFactory.Create(Fixture.ConnectionString).Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await svc.UpsertAsync(
                RegisterCode,
                RegisterName,
                ReferenceRegisterPeriodicity.NonPeriodic,
                ReferenceRegisterRecordMode.Independent,
                CancellationToken.None);
        }

        int baselineEvents;
        int baselineChanges;
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            baselineEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
            baselineChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");
        }

        const string dimensionCode = "it_dim_customer";
        var dimCodeNorm = dimensionCode.Trim().ToLowerInvariant();
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimCodeNorm}");

        var rules = new[]
        {
            new ReferenceRegisterDimensionRule(
                DimensionId: dimensionId,
                DimensionCode: dimensionCode,
                Ordinal: 10,
                IsRequired: false)
        };

        using var failingHost = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubjectRules,
                        Email: "audit.refreg.rules.rollback@example.com",
                        DisplayName: "Audit RefReg Rules Rollback")));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        // Act
        await using (var scope = failingHost.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            var act = () => svc.ReplaceDimensionRulesAsync(registerId, rules, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert
        await using var assertConn = new NpgsqlConnection(Fixture.ConnectionString);
        await assertConn.OpenAsync();

        var ruleCount = await assertConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM reference_register_dimension_rules WHERE register_id = @id;",
            new { id = registerId });
        ruleCount.Should().Be(0, "audit failure must rollback the dimension rules replace");

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
