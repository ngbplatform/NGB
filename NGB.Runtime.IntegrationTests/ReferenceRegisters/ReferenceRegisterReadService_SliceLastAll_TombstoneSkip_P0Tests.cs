using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterReadService_SliceLastAll_TombstoneSkip_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLastAll_SkipsTombstonesAcrossKeyPages_ToFillVisibleLimit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_LAST_ALL_SKIP_TS";
        var registerId = Guid.Empty;

        var buildingDimId = DeterministicGuid.Create("Dimension|building");

        // Arrange: metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "SliceLastAll Tombstone Skip",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceDimensionRulesAsync(
                registerId,
                [
                    new ReferenceRegisterDimensionRule(
                        DimensionId: buildingDimId,
                        DimensionCode: "building",
                        Ordinal: 10,
                        IsRequired: true)
                ],
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                [
                    // Nullable so tombstones can omit values.
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Int32, true),
                ],
                ct: CancellationToken.None);
        }

        var amountBySet = new Dictionary<Guid, int>();
        var setIds = new List<Guid>();

        // Arrange: records (30 keys, first 20 are tombstoned)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            for (var i = 1; i <= 30; i++)
            {
                var v = new DimensionValue(buildingDimId, DeterministicGuid.Create($"Building|{i:00}"));
                var setId = await dimSets.GetOrCreateIdAsync(new DimensionBag([v]), CancellationToken.None);

                setIds.Add(setId);
                amountBySet[setId] = i;
            }

            var writes = new List<ReferenceRegisterRecordWrite>(capacity: 60);

            // Initial versions for all keys
            foreach (var id in setIds)
            {
                writes.Add(new ReferenceRegisterRecordWrite(
                    DimensionSetId: id,
                    PeriodUtc: null,
                    RecorderDocumentId: null,
                    Values: new Dictionary<string, object?> { ["amount"] = amountBySet[id] }));
            }

            // Tombstone the first 20 keys in the key ordering (dimension_set_id ASC).
            var tombstoneIds = setIds.OrderBy(x => x).Take(20).ToArray();
            foreach (var id in tombstoneIds)
            {
                writes.Add(new ReferenceRegisterRecordWrite(
                    DimensionSetId: id,
                    PeriodUtc: null,
                    RecorderDocumentId: null,
                    Values: new Dictionary<string, object?>(),
                    IsDeleted: true));
            }

            await store.AppendAsync(registerId, writes, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            var visible = await read.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 10,
                includeDeleted: false,
                ct: CancellationToken.None);

            visible.Should().HaveCount(10, "service must keep fetching subsequent pages if first pages contain only tombstones");
            visible.All(x => !x.IsDeleted).Should().BeTrue();
            visible.Select(x => x.DimensionSetId).Should().BeInAscendingOrder();

            var expectedIds = setIds.OrderBy(x => x).Skip(20).Take(10).ToArray();
            visible.Select(x => x.DimensionSetId).Should().Equal(expectedIds);

            foreach (var r in visible)
                r.Values["amount"].Should().Be(amountBySet[r.DimensionSetId]);
        }
    }

    [Fact]
    public async Task SliceLastAllFiltered_SkipsTombstonesAcrossKeyPages_ToFillVisibleLimit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_LAST_ALL_SKIP_TS_FILTERED";
        var registerId = Guid.Empty;

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var siteDimId = DeterministicGuid.Create("Dimension|site");
        var siteValue = new DimensionValue(siteDimId, DeterministicGuid.Create("Site|S1"));

        // Arrange: metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "SliceLastAll Tombstone Skip Filtered",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceDimensionRulesAsync(
                registerId,
                [
                    new ReferenceRegisterDimensionRule(buildingDimId, "building", 10, true),
                    new ReferenceRegisterDimensionRule(siteDimId, "site", 20, true),
                ],
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Int32, true),
                ],
                ct: CancellationToken.None);
        }

        var amountBySet = new Dictionary<Guid, int>();
        var setIds = new List<Guid>();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            for (var i = 1; i <= 30; i++)
            {
                var buildingValue = new DimensionValue(buildingDimId, DeterministicGuid.Create($"Building|{i:00}"));
                var setId = await dimSets.GetOrCreateIdAsync(
                    new DimensionBag([siteValue, buildingValue]),
                    CancellationToken.None);

                setIds.Add(setId);
                amountBySet[setId] = i;
            }

            var writes = new List<ReferenceRegisterRecordWrite>(capacity: 60);

            foreach (var id in setIds)
            {
                writes.Add(new ReferenceRegisterRecordWrite(
                    DimensionSetId: id,
                    PeriodUtc: null,
                    RecorderDocumentId: null,
                    Values: new Dictionary<string, object?> { ["amount"] = amountBySet[id] }));
            }

            var tombstoneIds = setIds.OrderBy(x => x).Take(20).ToArray();
            foreach (var id in tombstoneIds)
            {
                writes.Add(new ReferenceRegisterRecordWrite(
                    DimensionSetId: id,
                    PeriodUtc: null,
                    RecorderDocumentId: null,
                    Values: new Dictionary<string, object?>(),
                    IsDeleted: true));
            }

            await store.AppendAsync(registerId, writes, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            var visible = await read.SliceLastAllFilteredAsync(
                registerId,
                asOfUtc,
                requiredDimensions: [siteValue],
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 10,
                includeDeleted: false,
                ct: CancellationToken.None);

            visible.Should().HaveCount(10);
            visible.All(x => !x.IsDeleted).Should().BeTrue();
            visible.Select(x => x.DimensionSetId).Should().BeInAscendingOrder();

            var expectedIds = setIds.OrderBy(x => x).Skip(20).Take(10).ToArray();
            visible.Select(x => x.DimensionSetId).Should().Equal(expectedIds);

            foreach (var r in visible)
                r.Values["amount"].Should().Be(amountBySet[r.DimensionSetId]);
        }
    }
}
