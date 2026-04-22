using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.Persistence.AuditLog;
using NGB.ReferenceRegisters;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using System.Text.Json;
using System.Text.Json.Serialization;
using NGB.Core.AuditLog;
using NGB.ReferenceRegisters.Contracts;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: ReferenceRegisterManagementService writes Business AuditLog events and is strictly no-op when unchanged.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterManagementService_Auditing_P0Tests(PostgresTestFixture fixture)
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
                        AuthSubject: "kc|refreg-user-1",
                        Email: "refreg.user@example.com",
                        DisplayName: "RefReg User")));
            });

        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code: "  RENT_ROLL ",
                name: " Rent Roll ",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterUpsert,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.ActorUserId.Should().NotBeNull();

            var user = await users.GetByAuthSubjectAsync("kc|refreg-user-1", CancellationToken.None);
            user.Should().NotBeNull();
            user!.UserId.Should().Be(ev.ActorUserId!.Value);

            ev.Changes.Select(c => c.FieldPath)
                .Should().Contain([
                    "code",
                    "name",
                    "table_code",
                    "periodicity",
                    "record_mode"
                ]);

            ev.Changes.Single(c => c.FieldPath == "code").NewValueJson.Should().Contain("RENT_ROLL");
            ev.Changes.Single(c => c.FieldPath == "name").NewValueJson.Should().Contain("Rent Roll");

            // Physical table token is stable and should be captured for diagnostics.
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
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync("RR", "Rent Roll", ReferenceRegisterPeriodicity.NonPeriodic,
                ReferenceRegisterRecordMode.Independent, CancellationToken.None);

            await svc.UpsertAsync("RR", "Rent Roll Updated", ReferenceRegisterPeriodicity.NonPeriodic,
                ReferenceRegisterRecordMode.Independent, CancellationToken.None);

            await svc.UpsertAsync("RR", "Rent Roll Updated", ReferenceRegisterPeriodicity.NonPeriodic,
                ReferenceRegisterRecordMode.Independent, CancellationToken.None); // strict no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterUpsert,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(2);

            // One event for create (old == null) and one event for update (old == "Rent Roll").
            events.Should().ContainSingle(ev =>
                ev.Changes.Any(c => c.FieldPath == "name"
                                   && c.OldValueJson != null
                                   && c.OldValueJson.Contains("Rent Roll")
                                   && c.NewValueJson != null
                                   && c.NewValueJson.Contains("Rent Roll Updated")));
        }
    }

    [Fact]
    public async Task ReplaceFields_WritesAuditOnce_AndSecondCallIsNoOp()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

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

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterReplaceFields,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);

            var ev = events.Single();
            ev.Changes.Should().ContainSingle(c => c.FieldPath == "fields");

            // Metadata should include the physical table token for diagnostics.
            ParseMetadata(ev.MetadataJson).Should().ContainKey("tableCode");
            ParseMetadata(ev.MetadataJson)["tableCode"].Should().Be("rr");

            var ch = ev.Changes.Single(c => c.FieldPath == "fields");

            ParseFields(ch.OldValueJson).Should().BeEmpty();

            ParseFields(ch.NewValueJson).Should().BeEquivalentTo(
                new[]
                {
                    new FieldJson(
                        CodeNorm: "amount",
                        ColumnCode: ReferenceRegisterNaming.NormalizeColumnCode("amount"),
                        Name: "Amount",
                        Ordinal: 10,
                        ColumnType: ColumnType.Decimal,
                        IsNullable: true),
                    new FieldJson(
                        CodeNorm: "note",
                        ColumnCode: ReferenceRegisterNaming.NormalizeColumnCode("note"),
                        Name: "Note",
                        Ordinal: 20,
                        ColumnType: ColumnType.String,
                        IsNullable: true)
                },
                o => o.WithStrictOrdering());
        }
    }

    [Fact]
    public async Task ReplaceDimensionRules_WritesAuditOnce_AndSecondCallIsNoOp()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

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

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ReferenceRegister,
                    EntityId: registerId,
                    ActionCode: AuditActionCodes.ReferenceRegisterReplaceDimensionRules,
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

            ParseRules(ch.OldValueJson).Should().BeEmpty();

            ParseRules(ch.NewValueJson).Should().BeEquivalentTo(
                new[]
                {
                    new RuleJson(dimBuildings, 10, true),
                    new RuleJson(dimUnits, 20, false)
                },
                o => o.WithStrictOrdering());
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed record RuleJson(Guid DimensionId, int Ordinal, bool IsRequired);

    private sealed record FieldJson(
        string CodeNorm,
        string ColumnCode,
        string Name,
        int Ordinal,
        ColumnType ColumnType,
        bool IsNullable);

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

    private static IReadOnlyList<FieldJson> ParseFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<FieldJson>();

        return JsonSerializer.Deserialize<List<FieldJson>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        })
               ?? [];
    }
}
