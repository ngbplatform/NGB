using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.AuditLog;
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
/// P0: OperationalRegisterManagementService resources management is audited and strictly no-op when unchanged.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterManagementService_Resources_Auditing_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReplaceResources_WritesAuditOnce_AndSecondCallIsNoOp()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

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

            // Same resources, different order -> should be treated as equivalent and be a strict no-op.
            await svc.ReplaceResourcesAsync(
                registerId,
                [
                    new OperationalRegisterResourceDefinition("Qty", "Quantity", 20),
                    new OperationalRegisterResourceDefinition("Amount", "Amount", 10)
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
                    ActionCode: AuditActionCodes.OperationalRegisterReplaceResources,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);

            var ev = events.Single();
            ev.Changes.Should().ContainSingle(c => c.FieldPath == "resources");

            var ch = ev.Changes.Single(c => c.FieldPath == "resources");
            ParseResources(ch.OldValueJson).Should().BeEmpty();

            ParseResources(ch.NewValueJson).Should().BeEquivalentTo(
                [
                    new ResourceJson("amount", "amount", "Amount", 10),
                    new ResourceJson("qty", "qty", "Quantity", 20)
                ],
                o => o.WithStrictOrdering());

            // Metadata should include physical table token.
            ParseMetadata(ev.MetadataJson).Should().ContainKey("tableCode");
            ParseMetadata(ev.MetadataJson)["tableCode"].Should().Be("rr");
        }
    }

    [Fact]
    public async Task ReplaceResources_WhenRegisterMissing_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var missingRegisterId = Guid.CreateVersion7();

        var act = async () => await svc.ReplaceResourcesAsync(
            missingRegisterId,
            [new OperationalRegisterResourceDefinition("Amount", "Amount", 10)],
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OperationalRegisterNotFoundException>();
        ex.Which.AssertNgbError(OperationalRegisterNotFoundException.Code, "registerId");
    }

    [Fact]
    public async Task ReplaceResources_WhenReservedColumnCode_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var registerId = await svc.UpsertAsync("RR", "Rent Roll", CancellationToken.None);

        var act = async () => await svc.ReplaceResourcesAsync(
            registerId,
            [
                // column_code becomes "document_id", which is reserved for fact tables.
                new OperationalRegisterResourceDefinition("document_id", "Document", 10)
            ],
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OperationalRegisterResourcesValidationException>();
        ex.Which.AssertNgbError(OperationalRegisterResourcesValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("reserved_column_code");
    }

    [Fact]
    public async Task ReplaceResources_WhenColumnCodeCollision_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var registerId = await svc.UpsertAsync("RR", "Rent Roll", CancellationToken.None);

        var act = async () => await svc.ReplaceResourcesAsync(
            registerId,
            [
                new OperationalRegisterResourceDefinition("Gross Amount", "Gross Amount", 10),
                new OperationalRegisterResourceDefinition("gross_amount", "Gross Amount 2", 20)
            ],
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<OperationalRegisterResourcesValidationException>();
        ex.Which.AssertNgbError(OperationalRegisterResourcesValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("column_code_collisions");
    }

    private sealed record ResourceJson(string CodeNorm, string ColumnCode, string Name, int Ordinal);

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

    private static IReadOnlyList<ResourceJson> ParseResources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        return JsonSerializer.Deserialize<List<ResourceJson>>(
                   json,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? [];
    }
}
