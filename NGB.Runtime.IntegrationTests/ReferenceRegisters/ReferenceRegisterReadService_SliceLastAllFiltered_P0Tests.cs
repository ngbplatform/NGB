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
public sealed class ReferenceRegisterReadService_SliceLastAllFiltered_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLastAllFiltered_FiltersBySingleOrMultipleDimensions_AndHonorsIncludeDeleted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_LAST_ALL_FILTERED";
        var registerId = Guid.Empty;

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var cpDimId = DeterministicGuid.Create("Dimension|counterparty");

        var bA = new DimensionValue(buildingDimId, DeterministicGuid.Create("Building|A"));
        var bB = new DimensionValue(buildingDimId, DeterministicGuid.Create("Building|B"));
        var cpX = new DimensionValue(cpDimId, DeterministicGuid.Create("Counterparty|X"));
        var cpY = new DimensionValue(cpDimId, DeterministicGuid.Create("Counterparty|Y"));

        // Arrange: register metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "SliceLastAllFiltered Test",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(
                registerId,
                [
                    new ReferenceRegisterDimensionRule(buildingDimId, "building", Ordinal: 10, IsRequired: true),
                    new ReferenceRegisterDimensionRule(cpDimId, "counterparty", Ordinal: 20, IsRequired: true),
                ],
                ct: CancellationToken.None);

            await mgmt.ReplaceFieldsAsync(
                registerId,
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true),
                ],
                ct: CancellationToken.None);
        }

        Guid setAX;
        Guid setAY;
        Guid setBX;

        // Act: append record versions for 3 keys; tombstone one key that matches the single-dimension filter
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            setAX = await dimSets.GetOrCreateIdAsync(new DimensionBag([bA, cpX]), CancellationToken.None);
            setAY = await dimSets.GetOrCreateIdAsync(new DimensionBag([bA, cpY]), CancellationToken.None);
            setBX = await dimSets.GetOrCreateIdAsync(new DimensionBag([bB, cpX]), CancellationToken.None);

            await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(setAX, PeriodUtc: null, RecorderDocumentId: null, Values: new Dictionary<string, object?> { ["amount"] = 1.0m }),
                    new ReferenceRegisterRecordWrite(setAY, PeriodUtc: null, RecorderDocumentId: null, Values: new Dictionary<string, object?> { ["amount"] = 2.0m }),
                    new ReferenceRegisterRecordWrite(setBX, PeriodUtc: null, RecorderDocumentId: null, Values: new Dictionary<string, object?> { ["amount"] = 3.0m }),

                    // Tombstone for key (building=A, counterparty=Y)
                    new ReferenceRegisterRecordWrite(setAY, PeriodUtc: null, RecorderDocumentId: null, Values: new Dictionary<string, object?>(), IsDeleted: true),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            // Filter by single dimension: building=A (should match AX and AY, but AY is tombstoned and hidden by default)
            var singleVisible = await read.SliceLastAllFilteredAsync(
                registerId,
                asOfUtc,
                requiredDimensions: [bA],
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: false,
                ct: CancellationToken.None);

            singleVisible.Select(x => x.DimensionSetId).Should().BeEquivalentTo([setAX]);

            var singleIncludingDeleted = await read.SliceLastAllFilteredAsync(
                registerId,
                asOfUtc,
                requiredDimensions: [bA],
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: true,
                ct: CancellationToken.None);

            singleIncludingDeleted.Select(x => x.DimensionSetId).Should().BeEquivalentTo([setAX, setAY]);
            singleIncludingDeleted.Single(x => x.DimensionSetId == setAY).IsDeleted.Should().BeTrue();

            // Filter by two dimensions: building=A AND counterparty=X (must match only AX)
            var pair = await read.SliceLastAllFilteredAsync(
                registerId,
                asOfUtc,
                requiredDimensions: [bA, cpX],
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: true,
                ct: CancellationToken.None);

            pair.Should().HaveCount(1);
            pair[0].DimensionSetId.Should().Be(setAX);
        }
    }

    [Fact]
    public async Task SliceLastAllFiltered_SupportsKeysetPagination_WithinFilteredKeys()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_LAST_ALL_FILTERED_PAGING";
        var registerId = Guid.Empty;

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var cpDimId = DeterministicGuid.Create("Dimension|counterparty");

        var bA = new DimensionValue(buildingDimId, DeterministicGuid.Create("Building|A"));
        var cp1 = new DimensionValue(cpDimId, DeterministicGuid.Create("Counterparty|1"));
        var cp2 = new DimensionValue(cpDimId, DeterministicGuid.Create("Counterparty|2"));
        var cp3 = new DimensionValue(cpDimId, DeterministicGuid.Create("Counterparty|3"));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "SliceLastAllFiltered Paging Test",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(
                registerId,
                [
                    new ReferenceRegisterDimensionRule(buildingDimId, "building", Ordinal: 10, IsRequired: true),
                    new ReferenceRegisterDimensionRule(cpDimId, "counterparty", Ordinal: 20, IsRequired: true),
                ],
                ct: CancellationToken.None);

            await mgmt.ReplaceFieldsAsync(
                registerId,
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true),
                ],
                ct: CancellationToken.None);
        }

        Guid set1;
        Guid set2;
        Guid set3;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            set1 = await dimSets.GetOrCreateIdAsync(new DimensionBag([bA, cp1]), CancellationToken.None);
            set2 = await dimSets.GetOrCreateIdAsync(new DimensionBag([bA, cp2]), CancellationToken.None);
            set3 = await dimSets.GetOrCreateIdAsync(new DimensionBag([bA, cp3]), CancellationToken.None);

            await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(set1, PeriodUtc: null, RecorderDocumentId: null, Values: new Dictionary<string, object?> { ["amount"] = 1.0m }),
                    new ReferenceRegisterRecordWrite(set2, PeriodUtc: null, RecorderDocumentId: null, Values: new Dictionary<string, object?> { ["amount"] = 2.0m }),
                    new ReferenceRegisterRecordWrite(set3, PeriodUtc: null, RecorderDocumentId: null, Values: new Dictionary<string, object?> { ["amount"] = 3.0m }),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            var all = new[] { set1, set2, set3 }.OrderBy(x => x).ToArray();

            var page1 = await read.SliceLastAllFilteredAsync(
                registerId,
                asOfUtc,
                requiredDimensions: [bA],
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 2,
                includeDeleted: true,
                ct: CancellationToken.None);

            page1.Should().HaveCount(2);
            page1[0].DimensionSetId.Should().Be(all[0]);
            page1[1].DimensionSetId.Should().Be(all[1]);

            var after = page1.Last().DimensionSetId;

            var page2 = await read.SliceLastAllFilteredAsync(
                registerId,
                asOfUtc,
                requiredDimensions: [bA],
                recorderDocumentId: null,
                afterDimensionSetId: after,
                limit: 10,
                includeDeleted: true,
                ct: CancellationToken.None);

            page2.Should().HaveCount(1);
            page2[0].DimensionSetId.Should().Be(all[2]);
            page2.Single().DimensionSetId.CompareTo(after).Should().BeGreaterThan(0);
        }
    }
}
