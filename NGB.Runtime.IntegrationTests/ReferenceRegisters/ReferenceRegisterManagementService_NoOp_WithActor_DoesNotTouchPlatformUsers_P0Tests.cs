using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Metadata.Base;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: strict no-op reference register management operations must NOT touch platform_users.
/// 
/// Rationale: actor upsert updates platform_users.updated_at_utc. If a no-op path still reaches AuditLogService,
/// it will silently mutate platform_users even when no audit event is emitted.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterManagementService_NoOp_WithActor_DoesNotTouchPlatformUsers_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Subject = "kc|refreg-noop-actor-p0";

    [Fact]
    public async Task UpsertAsync_WhenUnchanged_WithActor_IsNoOp_DoesNotTouchPlatformUsers()
    {
        using var host = CreateHostWithActor();

        Guid registerId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await svc.UpsertAsync(
                code: "RR",
                name: "Rent Roll",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);
        }

        var baseline = await CaptureBaselineAsync(registerId, AuditActionCodes.ReferenceRegisterUpsert);
        await Task.Delay(50);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            // strict no-op
            await svc.UpsertAsync(
                code: "RR",
                name: "Rent Roll",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);
        }

        await AssertNoOpAsync(registerId, AuditActionCodes.ReferenceRegisterUpsert, baseline);
    }

    [Fact]
    public async Task ReplaceFieldsAsync_WhenUnchanged_WithActor_IsNoOp_DoesNotTouchPlatformUsers()
    {
        using var host = CreateHostWithActor();

        Guid registerId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await svc.UpsertAsync(
                code: "RR",
                name: "Rent Roll",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, IsNullable: true),
                    new ReferenceRegisterFieldDefinition("note", "Note", 20, ColumnType.String, IsNullable: true)
                ],
                ct: CancellationToken.None);
        }

        var baseline = await CaptureBaselineAsync(registerId, AuditActionCodes.ReferenceRegisterReplaceFields);
        await Task.Delay(50);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            // Same fields, different order -> strict no-op.
            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("note", "Note", 20, ColumnType.String, IsNullable: true),
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, IsNullable: true)
                ],
                ct: CancellationToken.None);
        }

        await AssertNoOpAsync(registerId, AuditActionCodes.ReferenceRegisterReplaceFields, baseline);
    }

    [Fact]
    public async Task ReplaceDimensionRulesAsync_WhenUnchanged_WithActor_IsNoOp_DoesNotTouchPlatformUsers()
    {
        using var host = CreateHostWithActor();

        var dimBuildings = DeterministicGuid.Create("Dimension|buildings");
        var dimUnits = DeterministicGuid.Create("Dimension|units");

        Guid registerId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await svc.UpsertAsync(
                code: "RR",
                name: "Rent Roll",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceDimensionRulesAsync(
                registerId,
                rules:
                [
                    new ReferenceRegisterDimensionRule(dimBuildings, "Buildings", 10, IsRequired: true),
                    new ReferenceRegisterDimensionRule(dimUnits, "Units", 20, IsRequired: false)
                ],
                ct: CancellationToken.None);
        }

        var baseline = await CaptureBaselineAsync(registerId, AuditActionCodes.ReferenceRegisterReplaceDimensionRules);
        await Task.Delay(50);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            // Same rules, different order -> strict no-op.
            await svc.ReplaceDimensionRulesAsync(
                registerId,
                rules:
                [
                    new ReferenceRegisterDimensionRule(dimUnits, "Units", 20, IsRequired: false),
                    new ReferenceRegisterDimensionRule(dimBuildings, "Buildings", 10, IsRequired: true)
                ],
                ct: CancellationToken.None);
        }

        await AssertNoOpAsync(registerId, AuditActionCodes.ReferenceRegisterReplaceDimensionRules, baseline);
    }

    private record Baseline(int AuditEventsTotal, int AuditChangesTotal, int AuditEventsForEntityAndAction, DateTime PlatformUserUpdatedAtUtc);

    private async Task<Baseline> CaptureBaselineAsync(Guid entityId, string actionCode)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var totalEvents = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;");
        var totalChanges = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;");

        var eventsForEntity = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE entity_id = @id AND action_code = @ac;",
            new { id = entityId, ac = actionCode });

        var updatedAt = await conn.ExecuteScalarAsync<DateTime>(
            "SELECT updated_at_utc FROM platform_users WHERE auth_subject = @s;",
            new { s = Subject });

        return new Baseline(totalEvents, totalChanges, eventsForEntity, updatedAt);
    }

    private async Task AssertNoOpAsync(Guid entityId, string actionCode, Baseline baseline)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_events;"))
            .Should().Be(baseline.AuditEventsTotal);

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM platform_audit_event_changes;"))
            .Should().Be(baseline.AuditChangesTotal);

        (await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events WHERE entity_id = @id AND action_code = @ac;",
            new { id = entityId, ac = actionCode }))
            .Should().Be(baseline.AuditEventsForEntityAndAction);

        var updatedAtNow = await conn.ExecuteScalarAsync<DateTime>(
            "SELECT updated_at_utc FROM platform_users WHERE auth_subject = @s;",
            new { s = Subject });

        updatedAtNow.Should().Be(baseline.PlatformUserUpdatedAtUtc,
            "strict no-op must not even upsert the actor user (platform_users.updated_at_utc must remain unchanged)");
    }

    private IHost CreateHostWithActor()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: Subject,
                        Email: "refreg.noop.actor@example.com",
                        DisplayName: "RefReg NoOp Actor")));
            });

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
