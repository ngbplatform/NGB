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
public sealed class ReferenceRegisterReadService_SliceLastAll_NonPeriodic_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLastAll_ReturnsLatestVersion_PerKey_AndHidesTombstonesByDefault()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_LAST_ALL";
        var registerId = Guid.Empty;

        var dimId = DeterministicGuid.Create("Dimension|building");
        var aValueId = DeterministicGuid.Create("Building|A");
        var bValueId = DeterministicGuid.Create("Building|B");

        var a = new DimensionValue(dimId, aValueId);
        var b = new DimensionValue(dimId, bValueId);

        // Arrange: metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "SliceLastAll Test",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceDimensionRulesAsync(
                registerId,
                [
                    new ReferenceRegisterDimensionRule(
                        DimensionId: dimId,
                        DimensionCode: "building",
                        Ordinal: 10,
                        IsRequired: true)
                ],
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                [
                    // Nullable so tombstones can omit values.
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true),
                ],
                ct: CancellationToken.None);
        }

        // Act: append versions for two keys; tombstone one of them
        Guid aSetId;
        Guid bSetId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            aSetId = await dimSets.GetOrCreateIdAsync(new DimensionBag([a]), CancellationToken.None);
            bSetId = await dimSets.GetOrCreateIdAsync(new DimensionBag([b]), CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: aSetId,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 1.0m }),

                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: aSetId,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 2.0m }),

                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: bSetId,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 10.0m }),

                    // Tombstone for key B
                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: bSetId,
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

            var visible = await read.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: false,
                ct: CancellationToken.None);

            visible.Should().HaveCount(1, "tombstoned keys must be hidden by default");
            visible[0].DimensionSetId.Should().Be(aSetId);
            visible[0].IsDeleted.Should().BeFalse();
            visible[0].Values["amount"].Should().Be(2.0m);

            var included = await read.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId: null,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: true,
                ct: CancellationToken.None);

            included.Should().HaveCount(2);

            var tombstone = included.Single(x => x.DimensionSetId == bSetId);
            tombstone.IsDeleted.Should().BeTrue();
        }
    }
}
