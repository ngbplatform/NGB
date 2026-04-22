using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters
{
    /// <summary>
    /// P0: strict no-op operational register management operations must NOT touch platform_users.
    /// 
    /// Rationale: actor upsert updates platform_users.updated_at_utc. If a no-op path still reaches AuditLogService,
    /// it will silently mutate platform_users even when no audit event is emitted.
    /// </summary>
    [Collection(PostgresCollection.Name)]
    public sealed class OperationalRegisterManagementService_NoOp_WithActor_DoesNotTouchPlatformUsers_P0Tests(PostgresTestFixture fixture)
        : IntegrationTestBase(fixture)
    {
        private const string Subject = "kc|opreg-noop-actor-p0";

        [Fact]
        public async Task UpsertAsync_WhenUnchanged_WithActor_IsNoOp_DoesNotTouchPlatformUsers()
        {
            using var host = CreateHostWithActor();

            Guid registerId;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
                registerId = await svc.UpsertAsync("RR", "Rent Roll", CancellationToken.None);
            }

            var baseline = await CaptureBaselineAsync(registerId, AuditActionCodes.OperationalRegisterUpsert);
            await Task.Delay(50);

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

                // strict no-op
                await svc.UpsertAsync("RR", "Rent Roll", CancellationToken.None);
            }

            await AssertNoOpAsync(registerId, AuditActionCodes.OperationalRegisterUpsert, baseline);
        }

        [Fact]
        public async Task ReplaceResourcesAsync_WhenUnchanged_WithActor_IsNoOp_DoesNotTouchPlatformUsers()
        {
            using var host = CreateHostWithActor();

            Guid registerId;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
                registerId = await svc.UpsertAsync("RR", "Rent Roll", CancellationToken.None);

                await svc.ReplaceResourcesAsync(
                    registerId,
                    [
                        new OperationalRegisterResourceDefinition("Amount", "Amount", 10),
                        new OperationalRegisterResourceDefinition("Qty", "Quantity", 20)
                    ],
                    CancellationToken.None);
            }

            var baseline = await CaptureBaselineAsync(registerId, AuditActionCodes.OperationalRegisterReplaceResources);
            await Task.Delay(50);

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

                // Same resources, different order -> strict no-op.
                await svc.ReplaceResourcesAsync(
                    registerId,
                    [
                        new OperationalRegisterResourceDefinition("Qty", "Quantity", 20),
                        new OperationalRegisterResourceDefinition("Amount", "Amount", 10)
                    ],
                    CancellationToken.None);
            }

            await AssertNoOpAsync(registerId, AuditActionCodes.OperationalRegisterReplaceResources, baseline);
        }

        [Fact]
        public async Task ReplaceDimensionRulesAsync_WhenUnchanged_WithActor_IsNoOp_DoesNotTouchPlatformUsers()
        {
            using var host = CreateHostWithActor();

            // Seed dimensions.
            var dimId1 = Guid.CreateVersion7();
            var dimId2 = Guid.CreateVersion7();
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                await uow.BeginTransactionAsync(CancellationToken.None);

                await uow.Connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, @Name);",
                    new[]
                    {
                        new { Id = dimId1, Code = "Buildings", Name = "Buildings" },
                        new { Id = dimId2, Code = "Units", Name = "Units" }
                    },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

                await uow.CommitAsync(CancellationToken.None);
            }

            Guid registerId;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
                registerId = await svc.UpsertAsync("RR", "Rent Roll", CancellationToken.None);

                await svc.ReplaceDimensionRulesAsync(
                    registerId,
                    [
                        new OperationalRegisterDimensionRule(dimId1, "Buildings", 10, true),
                        new OperationalRegisterDimensionRule(dimId2, "Units", 20, false)
                    ],
                    CancellationToken.None);
            }

            var baseline = await CaptureBaselineAsync(registerId, AuditActionCodes.OperationalRegisterReplaceDimensionRules);
            await Task.Delay(50);

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

                // Same rules, different order -> strict no-op.
                await svc.ReplaceDimensionRulesAsync(
                    registerId,
                    [
                        new OperationalRegisterDimensionRule(dimId2, "Units", 20, false),
                        new OperationalRegisterDimensionRule(dimId1, "Buildings", 10, true)
                    ],
                    CancellationToken.None);
            }

            await AssertNoOpAsync(registerId, AuditActionCodes.OperationalRegisterReplaceDimensionRules, baseline);
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
                            Email: "opreg.noop@example.com",
                            DisplayName: "OpReg NoOp Actor")));
                });
    
        sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
        {
            public ActorIdentity? Current => actor;
        }
    }
}
