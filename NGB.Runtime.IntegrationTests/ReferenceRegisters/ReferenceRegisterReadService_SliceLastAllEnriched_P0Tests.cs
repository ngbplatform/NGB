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
public sealed class ReferenceRegisterReadService_SliceLastAllEnriched_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLastAllEnriched_ReturnsResolvedDimensionsAndDisplays_AndHonorsIncludeDeleted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_ENRICHED_STATE";
        var registerId = Guid.Empty;

        // Use unique dimension codes that are NOT registered as catalogs, to force enrichment fallback to short-guid.
        var buildingDimId = DeterministicGuid.Create("Dimension|rr_building_enriched");
        var unitDimId = DeterministicGuid.Create("Dimension|rr_unit_enriched");

        var bA = new DimensionValue(buildingDimId, DeterministicGuid.Create("RR_ENRICHED|Building|A"));
        var u101 = new DimensionValue(unitDimId, DeterministicGuid.Create("RR_ENRICHED|Unit|101"));

        var bB = new DimensionValue(buildingDimId, DeterministicGuid.Create("RR_ENRICHED|Building|B"));
        var u102 = new DimensionValue(unitDimId, DeterministicGuid.Create("RR_ENRICHED|Unit|102"));

        // Arrange: register metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "Enriched SliceLastAll Test",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(
                registerId,
                [
                    new ReferenceRegisterDimensionRule(buildingDimId, "rr_building_enriched", Ordinal: 10, IsRequired: true),
                    new ReferenceRegisterDimensionRule(unitDimId, "rr_unit_enriched", Ordinal: 20, IsRequired: true),
                ],
                ct: CancellationToken.None);

            await mgmt.ReplaceFieldsAsync(
                registerId,
                [new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true)],
                ct: CancellationToken.None);
        }

        Guid setA;
        Guid setB;

        // Act: append 2 keys; make one key a tombstone (latest version is_deleted=true)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            setA = await dimSets.GetOrCreateIdAsync(new DimensionBag([bA, u101]), CancellationToken.None);
            setB = await dimSets.GetOrCreateIdAsync(new DimensionBag([bB, u102]), CancellationToken.None);

            await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(
                        setA,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 1.0m }),

                    new ReferenceRegisterRecordWrite(
                        setB,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 2.0m }),

                    // tombstone for setB
                    new ReferenceRegisterRecordWrite(
                        setB,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?>(),
                        IsDeleted: true),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            // Default: tombstones hidden
            var visible = await read.SliceLastAllEnrichedAsync(
                registerId,
                asOfUtc,
                requiredDimensions: null,
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: false,
                ct: CancellationToken.None);

            visible.Should().HaveCount(1);
            var only = visible.Single();

            only.Record.DimensionSetId.Should().Be(setA);
            only.Record.IsDeleted.Should().BeFalse();
            ((decimal)only.Record.Values["amount"]!).Should().Be(1.0m);

            only.Dimensions.Items.Should().BeEquivalentTo(new[] { bA, u101 });

            // Displays: we accept catalog/document enrichment, but always have a fallback short-guid.
            var expectedBuilding = bA.ValueId.ToString("N")[..8];
            var expectedUnit = u101.ValueId.ToString("N")[..8];

            only.DimensionValueDisplaysByDimensionId.Should().ContainKey(buildingDimId);
            only.DimensionValueDisplaysByDimensionId.Should().ContainKey(unitDimId);

            only.DimensionValueDisplaysByDimensionId[buildingDimId].Should().NotBeNullOrWhiteSpace();
            only.DimensionValueDisplaysByDimensionId[unitDimId].Should().NotBeNullOrWhiteSpace();

            // If enrichment didn't resolve, we must see short-guid; if it did resolve, it will differ (that's ok).
            only.DimensionValueDisplaysByDimensionId[buildingDimId].Length.Should().BeGreaterThanOrEqualTo(8);
            only.DimensionValueDisplaysByDimensionId[unitDimId].Length.Should().BeGreaterThanOrEqualTo(8);

            // Enforce the fallback contract (8 chars) when no enrichment exists.
            // The deterministic value ids used here are very unlikely to exist in any catalog.
            only.DimensionValueDisplaysByDimensionId[buildingDimId].Should().Be(expectedBuilding);
            only.DimensionValueDisplaysByDimensionId[unitDimId].Should().Be(expectedUnit);

            // includeDeleted: tombstone should be visible
            var withDeleted = await read.SliceLastAllEnrichedAsync(
                registerId,
                asOfUtc,
                requiredDimensions: null,
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: true,
                ct: CancellationToken.None);

            withDeleted.Should().HaveCount(2);
            withDeleted.Single(x => x.Record.DimensionSetId == setB).Record.IsDeleted.Should().BeTrue();
        }
    }

    [Fact]
    public async Task SliceLastAllEnriched_SupportsFilteringAndKeysetPagination()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_ENRICHED_FILTER_PAGINATION";
        var registerId = Guid.Empty;

        var buildingDimId = DeterministicGuid.Create("Dimension|rr_building_pag");
        var bA = new DimensionValue(buildingDimId, DeterministicGuid.Create("RR_ENRICHED|Building|A"));
        var bB = new DimensionValue(buildingDimId, DeterministicGuid.Create("RR_ENRICHED|Building|B"));

        // Arrange
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "Enriched Filter+Pagination Test",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(
                registerId,
                [new ReferenceRegisterDimensionRule(buildingDimId, "rr_building_pag", Ordinal: 10, IsRequired: true)],
                ct: CancellationToken.None);

            await mgmt.ReplaceFieldsAsync(
                registerId,
                [new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true)],
                ct: CancellationToken.None);
        }

        Guid setA;
        Guid setB;

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            setA = await dimSets.GetOrCreateIdAsync(new DimensionBag([bA]), CancellationToken.None);
            setB = await dimSets.GetOrCreateIdAsync(new DimensionBag([bB]), CancellationToken.None);

            await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(setA, PeriodUtc: null, RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 10.0m }),
                    new ReferenceRegisterRecordWrite(setB, PeriodUtc: null, RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 20.0m }),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            // Filtering: only Building A
            var filtered = await read.SliceLastAllEnrichedAsync(
                registerId,
                asOfUtc,
                requiredDimensions: [bA],
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: false,
                ct: CancellationToken.None);

            filtered.Should().HaveCount(1);
            filtered[0].Record.DimensionSetId.Should().Be(setA);

            // Keyset pagination (dimension_set_id)
            var all = await read.SliceLastAllEnrichedAsync(
                registerId,
                asOfUtc,
                requiredDimensions: null,
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: false,
                ct: CancellationToken.None);

            all.Should().HaveCount(2);

            var ordered = all.OrderBy(x => x.Record.DimensionSetId).ToArray();
            var after = ordered[0].Record.DimensionSetId;

            var page2 = await read.SliceLastAllEnrichedAsync(
                registerId,
                asOfUtc,
                requiredDimensions: null,
                recorderDocumentId: null,
                afterDimensionSetId: after,
                limit: 100,
                includeDeleted: false,
                ct: CancellationToken.None);

            page2.Should().HaveCount(1);
            page2[0].Record.DimensionSetId.CompareTo(after).Should().BeGreaterThan(0);
        }
    }
}
