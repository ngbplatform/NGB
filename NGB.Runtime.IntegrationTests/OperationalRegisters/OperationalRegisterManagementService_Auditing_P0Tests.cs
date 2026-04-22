using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using System.Text.Json;
using NGB.Core.AuditLog;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: OperationalRegisterManagementService writes Business AuditLog events and is strictly no-op when unchanged.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterManagementService_Auditing_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Upsert_Create_WritesAuditEvent_AndUpsertsActor()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|opreg-user-1",
                        Email: "opreg.user@example.com",
                        DisplayName: "OpReg User")));
            });

        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            registerId = await svc.UpsertAsync("  RENT_ROLL ", " Rent Roll ", CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.OperationalRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.OperationalRegisterUpsert,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.ActorUserId.Should().NotBeNull();

            var user = await users.GetByAuthSubjectAsync("kc|opreg-user-1", CancellationToken.None);
            user.Should().NotBeNull();
            user!.UserId.Should().Be(ev.ActorUserId!.Value);

            ev.Changes.Select(c => c.FieldPath).Should().Contain(["code", "name"]);
            ev.Changes.Single(c => c.FieldPath == "code").NewValueJson.Should().Contain("RENT_ROLL");
            ev.Changes.Single(c => c.FieldPath == "name").NewValueJson.Should().Contain("Rent Roll");

            // Physical table token is stable and should be captured for diagnostics.
            ev.Changes.Select(c => c.FieldPath).Should().Contain("table_code");
            ev.Changes.Single(c => c.FieldPath == "table_code").NewValueJson.Should().Contain("rent_roll");

            // Metadata is camelCase JSON.
            ParseMetadata(ev.MetadataJson).Should().ContainKey("tableCode");
            ParseMetadata(ev.MetadataJson)["tableCode"].Should().Be("rent_roll");
        }
    }

    [Fact]
    public async Task Upsert_Update_Then_NoOp_WritesOnlyTwoAuditEvents()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            registerId = await svc.UpsertAsync("RR", "Rent Roll", CancellationToken.None);
            await svc.UpsertAsync("RR", "Rent Roll Updated", CancellationToken.None);
            await svc.UpsertAsync("RR", "Rent Roll Updated", CancellationToken.None); // strict no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.OperationalRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.OperationalRegisterUpsert,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(2);

            // One event for create (old == null) and one event for update (old == "Rent Roll").
            events.Should().ContainSingle(ev =>
                ev.Changes.Any(c => c.FieldPath == "name" && c.OldValueJson != null && c.OldValueJson.Contains("Rent Roll")));
        }
    }

    [Fact]
    public async Task ReplaceDimensionRules_WritesAuditOnce_AndSecondCallIsNoOp()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.Empty;
        var dimId1 = Guid.CreateVersion7();
        var dimId2 = Guid.CreateVersion7();

        // Seed dimensions.
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

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            registerId = await svc.UpsertAsync("RR", "Rent Roll", CancellationToken.None);

            await svc.ReplaceDimensionRulesAsync(registerId,
                [
                    new OperationalRegisterDimensionRule(dimId1, "Buildings", 10, true),
                    new OperationalRegisterDimensionRule(dimId2, "Units", 20, false)
                ],
                CancellationToken.None);

            // Same rules, different order -> should be treated as equivalent and be a strict no-op.
            await svc.ReplaceDimensionRulesAsync(registerId,
                [
                    new OperationalRegisterDimensionRule(dimId2, "Units", 20, false),
                    new OperationalRegisterDimensionRule(dimId1, "Buildings", 10, true)
                ],
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.OperationalRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.OperationalRegisterReplaceDimensionRules,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);

            var ev = events.Single();
            ev.Changes.Should().ContainSingle(c => c.FieldPath == "dimension_rules");

            // Metadata should include the physical table token for diagnostics.
            ParseMetadata(ev.MetadataJson).Should().ContainKey("tableCode");
            ParseMetadata(ev.MetadataJson)["tableCode"].Should().Be("rr");

            var ch = ev.Changes.Single(c => c.FieldPath == "dimension_rules");

            // Assert structurally (ignore JSON whitespace / formatting).
            ParseRules(ch.OldValueJson).Should().BeEmpty();
            ParseRules(ch.NewValueJson).Should().BeEquivalentTo(
                new[]
                {
                    new RuleJson(dimId1, 10, true),
                    new RuleJson(dimId2, 20, false)
                },
                o => o.WithStrictOrdering());
        }
    }

    [Fact]
    public async Task ReplaceDimensionRules_WhenRegisterMissing_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var missingRegisterId = Guid.CreateVersion7();

        var act = async () => await svc.ReplaceDimensionRulesAsync(
            missingRegisterId,
            [],
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OperationalRegisterNotFoundException>();
        ex.Which.AssertNgbError(OperationalRegisterNotFoundException.Code, "registerId");
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed record RuleJson(Guid DimensionId, int Ordinal, bool IsRequired);

    private static IReadOnlyDictionary<string, string> ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>();

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in root.EnumerateObject())
        {
            dict[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? string.Empty,
                _ => p.Value.GetRawText()
            };
        }

        return dict;
    }

    private static IReadOnlyList<RuleJson> ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<RuleJson>();

        return JsonSerializer.Deserialize<List<RuleJson>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? [];
    }
}
