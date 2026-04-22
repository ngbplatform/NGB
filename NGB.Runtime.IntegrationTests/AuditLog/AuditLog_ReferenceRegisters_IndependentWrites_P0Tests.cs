using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Metadata.Base;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

/// <summary>
/// P0: Independent-mode Reference Register writes emit high-level Business AuditLog events
/// (same principle as Operational Registers: no per-row audit for recorder-mode writes; independent writes must be audited).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditLog_ReferenceRegisters_IndependentWrites_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task IndependentUpsert_WritesAuditEvent_AndUpsertsActor()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|refreg-user-1",
                        Email: "refreg.user@example.com",
                        DisplayName: "RefReg User")));
            });

        Guid registerId;
        var commandId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await admin.UpsertAsync(
                code: "RR_AUDIT_UPSERT_1",
                name: "RR Audit Upsert 1",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();
            var res = await svc.UpsertByDimensionSetIdAsync(
                registerId,
                dimensionSetId: Guid.Empty,
                periodUtc: null,
                values: new Dictionary<string, object?>(),
                commandId: commandId,
                manageTransaction: true,
                ct: CancellationToken.None);

            res.Should().Be(ReferenceRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterRecordsUpsert,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.ActorUserId.Should().NotBeNull();

            var user = await users.GetByAuthSubjectAsync("kc|refreg-user-1", CancellationToken.None);
            user.Should().NotBeNull();
            user!.UserId.Should().Be(ev.ActorUserId!.Value);

            ev.Changes.Select(c => c.FieldPath).Should().Contain(["is_deleted", "values"]);
            ev.Changes.Single(c => c.FieldPath == "is_deleted").NewValueJson.Should().Contain("false");
        }
    }

    [Fact]
    public async Task IndependentUpsert_SameCommandId_IsIdempotent_AndDoesNotWriteSecondAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;
        var commandId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await admin.UpsertAsync(
                code: "RR_AUDIT_UPSERT_2",
                name: "RR Audit Upsert 2",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            await svc.UpsertByDimensionSetIdAsync(registerId, Guid.Empty, null, new Dictionary<string, object?>(), commandId,
                manageTransaction: true, ct: CancellationToken.None);
            await svc.UpsertByDimensionSetIdAsync(registerId, Guid.Empty, null, new Dictionary<string, object?>(), commandId,
                manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterRecordsUpsert,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task IndependentUpsert_Periodic_BackdatedPeriods_UsesEffectiveOldState_InAuditDiff()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;

        var p1 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var p2 = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var cmd1 = Guid.CreateVersion7();
        var cmd2 = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await admin.UpsertAsync(
                code: "RR_AUDIT_UPSERT_PERIODIC_1",
                name: "RR Audit Upsert Periodic 1",
                periodicity: ReferenceRegisterPeriodicity.Day,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await admin.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition(
                        Code: "amount",
                        Name: "Amount",
                        Ordinal: 10,
                        ColumnType: ColumnType.Int32,
                        IsNullable: false)
                ],
                ct: CancellationToken.None);

            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            await svc.UpsertByDimensionSetIdAsync(
                registerId,
                dimensionSetId: Guid.Empty,
                periodUtc: p1,
                values: new Dictionary<string, object?> { ["amount"] = 10 },
                commandId: cmd1,
                manageTransaction: true,
                ct: CancellationToken.None);

            await svc.UpsertByDimensionSetIdAsync(
                registerId,
                dimensionSetId: Guid.Empty,
                periodUtc: p2,
                values: new Dictionary<string, object?> { ["amount"] = 20 },
                commandId: cmd2,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterRecordsUpsert,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(2);

            // The second write should see the first write as the effective old state (even though the write is backdated).
            var ev = events.OrderBy(e => e.OccurredAtUtc).Last();
            ev.Changes.Select(c => c.FieldPath).Should().Contain(["values"]);

            var values = ev.Changes.Single(c => c.FieldPath == "values");
            values.OldValueJson.Should().NotBeNull();
            values.OldValueJson!.Should().Contain("10");
            values.NewValueJson.Should().Contain("20");
        }
    }

    [Fact]
    public async Task IndependentTombstone_WhenRecordExists_WritesAuditEvent_AndIsIdempotent()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;
        var upsertCommandId = Guid.CreateVersion7();
        var tombstoneCommandId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await admin.UpsertAsync(
                code: "RR_AUDIT_TOMBSTONE_1",
                name: "RR Audit Tombstone 1",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            await svc.UpsertByDimensionSetIdAsync(registerId, Guid.Empty, null, new Dictionary<string, object?>(), upsertCommandId,
                manageTransaction: true, ct: CancellationToken.None);

            var asOfUtc = DateTime.UtcNow;

            await svc.TombstoneByDimensionSetIdAsync(registerId, Guid.Empty, asOfUtc, tombstoneCommandId,
                manageTransaction: true, ct: CancellationToken.None);

            // idempotent no-op
            await svc.TombstoneByDimensionSetIdAsync(registerId, Guid.Empty, asOfUtc, tombstoneCommandId,
                manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterRecordsTombstone,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
            events.Single().Changes.Single(c => c.FieldPath == "is_deleted").NewValueJson.Should().Contain("true");
        }
    }

    [Fact]
    public async Task IndependentTombstone_WhenNoRecord_IsNoOp_AndDoesNotWriteAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;
        var tombstoneCommandId = Guid.CreateVersion7();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            registerId = await admin.UpsertAsync(
                code: "RR_AUDIT_TOMBSTONE_2",
                name: "RR Audit Tombstone 2",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

            await svc.TombstoneByDimensionSetIdAsync(registerId, Guid.Empty, DateTime.UtcNow, tombstoneCommandId,
                manageTransaction: true, ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterRecordsTombstone,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().BeEmpty();
        }
    }

    sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
