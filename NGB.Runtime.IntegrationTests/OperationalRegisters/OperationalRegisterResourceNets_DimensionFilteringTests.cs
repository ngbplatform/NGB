using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.OperationalRegisters;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(RegistersPostgresCollection.Name)]
public sealed class OperationalRegisterResourceNets_DimensionFilteringTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partial_filters_include_supersets_preserve_storno_and_add_snapshot(bool withSnapshot)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var uow = services.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        var registerId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await services.GetRequiredService<IOperationalRegisterRepository>().UpsertAsync(
            new OperationalRegisterUpsert(registerId, "it_dimension_net", "Dimension net"), now);
        await services.GetRequiredService<IOperationalRegisterResourceRepository>().ReplaceAsync(
            registerId, [new OperationalRegisterResourceDefinition("amount", "Amount", 1)], now);
        var dimensions = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        var values = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO platform_dimensions(dimension_id,code,name) VALUES (@Id,@Code,@Code)",
            dimensions.Select((id,index) => new { Id=id, Code=$"it_net_dimension_{index}" }), uow.Transaction));
        var sets = services.GetRequiredService<IDimensionSetService>();
        var filter = dimensions.Take(4).Select((id,index) => new DimensionValue(id,values[index])).ToArray();
        var first = await sets.GetOrCreateIdAsync(new DimensionBag(filter));
        var superset = await sets.GetOrCreateIdAsync(new DimensionBag([.. filter, new(dimensions[4],values[4])]));
        var otherItem = await sets.GetOrCreateIdAsync(new DimensionBag([.. filter.Take(3), new(dimensions[3],Guid.NewGuid())]));
        var otherScope = await sets.GetOrCreateIdAsync(new DimensionBag([new(dimensions[0],Guid.NewGuid()), .. filter.Skip(1)]));
        var movements = services.GetRequiredService<IOperationalRegisterMovementsStore>();
        await movements.EnsureSchemaAsync(registerId);
        var date = new DateTime(2026,9,10,0,0,0,DateTimeKind.Utc);
        var reversedDocument = Guid.CreateVersion7();
        await movements.AppendAsync(registerId,
        [
            new(Guid.CreateVersion7(),date,first,new Dictionary<string,decimal>{{"amount",10m}}),
            new(Guid.CreateVersion7(),date,superset,new Dictionary<string,decimal>{{"amount",20m}}),
            new(Guid.CreateVersion7(),date,otherItem,new Dictionary<string,decimal>{{"amount",40m}}),
            new(Guid.CreateVersion7(),date,otherScope,new Dictionary<string,decimal>{{"amount",80m}}),
            new(reversedDocument,date,superset,new Dictionary<string,decimal>{{"amount",7m}})
        ]);
        await movements.AppendStornoByDocumentAsync(registerId,reversedDocument);
        if (withSnapshot)
        {
            var month = new DateOnly(2026,8,1);
            await services.GetRequiredService<IOperationalRegisterBalancesStore>().ReplaceForMonthAsync(
                registerId,month,
                [new(first,new Dictionary<string,decimal>{{"amount",1000m}}),
                 new(superset,new Dictionary<string,decimal>{{"amount",2000m}})]);
            await services.GetRequiredService<IOperationalRegisterFinalizationRepository>()
                .MarkFinalizedAsync(registerId,month,now,now);
        }
        var net = services.GetRequiredService<IOperationalRegisterResourceNetReader>();
        var baseline = withSnapshot ? 3000m : 0m;
        (await net.GetNetByDimensionsAsync(registerId,filter,"amount")).Should().Be(baseline+30m);
        (await net.GetNetByDimensionsAsync(registerId,filter.Reverse().ToArray(),"amount")).Should().Be(baseline+30m);
        (await net.GetNetByDimensionsAsync(registerId,filter.Take(3).ToArray(),"amount")).Should().Be(baseline+70m);
        (await net.GetNetByDimensionsAsync(registerId,[filter[3]],"amount")).Should().Be(baseline+110m);
        (await net.GetNetByDimensionsAsync(registerId,[new(dimensions[3],Guid.NewGuid())],"amount")).Should().Be(0m);
        await uow.RollbackAsync();
    }

    [Fact]
    public async Task Selective_item_in_common_scope_does_not_scan_all_scope_members()
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        var dimensions = Enumerable.Range(0,4).Select(_ => Guid.CreateVersion7()).ToArray();
        var scopeValues = Enumerable.Range(0,3).Select(_ => Guid.CreateVersion7()).ToArray();
        var setIds = Enumerable.Range(0,20000).Select(_ => Guid.CreateVersion7()).ToArray();
        await connection.ExecuteAsync(
            "INSERT INTO platform_dimensions(dimension_id,code,name) VALUES (@Id,@Code,@Code)",
            dimensions.Select((id,index) => new { Id=id, Code=$"it_net_plan_{index}" }));
        await connection.ExecuteAsync("""
            INSERT INTO platform_dimension_sets(dimension_set_id) SELECT unnest(@SetIds::uuid[]);
            INSERT INTO platform_dimension_set_items(dimension_set_id,dimension_id,value_id)
            SELECT s.id,d.id,CASE WHEN d.ordinal=4 THEN s.id ELSE (@ScopeValues::uuid[])[d.ordinal::integer] END
            FROM unnest(@SetIds::uuid[]) s(id)
            CROSS JOIN unnest(@Dimensions::uuid[]) WITH ORDINALITY d(id,ordinal);
            ANALYZE platform_dimension_set_items;
            """,new { SetIds=setIds, Dimensions=dimensions, ScopeValues=scopeValues });

        var selected = setIds[123];
        Guid[] requestedValues = [.. scopeValues,selected];
        // Input order must not force the planner to start from a common scope.
        foreach (var reversed in new[] {false,true})
        {
            var parameters = new
            {
                DimensionIds = reversed ? dimensions.Reverse().ToArray() : dimensions,
                ValueIds = reversed ? requestedValues.Reverse().ToArray() : requestedValues
            };
            var sql = PostgresOperationalRegisterResourceNetReader.BuildMatchingDimensionSetsSql(4);
            (await connection.QueryAsync<Guid>(sql,parameters)).Should().Equal(selected);
            var planJson = await connection.QuerySingleAsync<string>(
                "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql,parameters);
            using var plan = JsonDocument.Parse(planJson);
            var scans = Nodes(plan.RootElement[0].GetProperty("Plan"))
                .Where(node => node.TryGetProperty("Relation Name", out var relation)
                    && relation.GetString() == "platform_dimension_set_items").ToArray();
            scans.Should().NotBeEmpty();
            // Actual Rows alone counts only the output: a full scan returning one
            // row must not pass this regression guard by filtering all others out.
            var scannedRows = scans.Sum(node =>
                (node.GetProperty("Actual Rows").GetDouble()
                 + (node.TryGetProperty("Rows Removed by Filter", out var filtered) ? filtered.GetDouble() : 0)
                 + (node.TryGetProperty("Rows Removed by Index Recheck", out var rechecked) ? rechecked.GetDouble() : 0))
                * node.GetProperty("Actual Loops").GetDouble());
            scannedRows.Should().BeLessThan(100,
                "a single item lookup must not process the 20,000 other items sharing its scope");
        }
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        yield return node;
        if (!node.TryGetProperty("Plans",out var children)) yield break;
        foreach (var child in children.EnumerateArray())
            foreach (var descendant in Nodes(child)) yield return descendant;
    }
}
