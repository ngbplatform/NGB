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
public sealed class ReferenceRegisterReadService_SliceLastAll_CursorAwarePaging_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLastAllPage_ReturnsCursorAdvancedByLastExaminedKey_WhenSkippingTombstones()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_LAST_ALL_PAGE_CURSOR";
        var registerId = Guid.Empty;

        var buildingDimId = DeterministicGuid.Create("Dimension|building");

        // Arrange: metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "SliceLastAll Cursor-Aware Paging",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceDimensionRulesAsync(
                registerId,
                [new ReferenceRegisterDimensionRule(buildingDimId, "building", 10, true)],
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

        // Arrange: records (30 keys, first 6 are tombstoned, limit=10 will fill from page 2 and stop early)
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

            var ordered = setIds.OrderBy(x => x).ToArray();

            var writes = new List<ReferenceRegisterRecordWrite>(capacity: 60);

            // Initial versions for all keys
            foreach (var id in ordered)
            {
                writes.Add(new ReferenceRegisterRecordWrite(
                    DimensionSetId: id,
                    PeriodUtc: null,
                    RecorderDocumentId: null,
                    Values: new Dictionary<string, object?> { ["amount"] = amountBySet[id] }));
            }

            // Tombstone first 6 keys in ordering.
            foreach (var id in ordered.Take(6))
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
            var ordered = setIds.OrderBy(x => x).ToArray();

            var page1 = await read.SliceLastAllPageAsync(
                registerId,
                asOfUtc,
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 10,
                includeDeleted: false,
                ct: CancellationToken.None);

            page1.Records.Should().HaveCount(10);
            page1.Records.All(x => !x.IsDeleted).Should().BeTrue();
            page1.Records.Select(x => x.DimensionSetId).Should().BeInAscendingOrder();

            // Visible keys are 7..16
            var expectedVisible1 = ordered.Skip(6).Take(10).ToArray();
            page1.Records.Select(x => x.DimensionSetId).Should().Equal(expectedVisible1);

            // Cursor must advance by the last examined key, not by the last returned visible key.
            // We expect it to move to the end of the second persistence page (key #20).
            var expectedCursor1 = ordered[19];
            page1.NextAfterDimensionSetId.Should().Be(expectedCursor1);

            // Next page using the cursor must not repeat keys 17..20.
            var page2 = await read.SliceLastAllPageAsync(
                registerId,
                asOfUtc,
                recorderDocumentId: null,
                afterDimensionSetId: page1.NextAfterDimensionSetId,
                limit: 10,
                includeDeleted: false,
                ct: CancellationToken.None);

            page2.Records.Should().HaveCount(10);
            page2.Records.Select(x => x.DimensionSetId).Should().Equal(ordered.Skip(20).Take(10).ToArray());

            var overlap = page1.Records.Select(x => x.DimensionSetId).Intersect(page2.Records.Select(x => x.DimensionSetId)).ToArray();
            overlap.Should().BeEmpty();
        }
    }
}
