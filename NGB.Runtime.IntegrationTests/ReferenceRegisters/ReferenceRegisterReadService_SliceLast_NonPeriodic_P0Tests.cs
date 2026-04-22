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
public sealed class ReferenceRegisterReadService_SliceLast_NonPeriodic_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLast_ReturnsLatestVersion_ForDimensionSet()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_LAST";
        var registerId = Guid.Empty;

        var dimId = DeterministicGuid.Create("Dimension|building");
        var valueId = DeterministicGuid.Create("Building|A");
        var dimValue = new DimensionValue(dimId, valueId);

        // Arrange: metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "SliceLast Test",
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

        // Act: append two versions
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var setId = await dimSets.GetOrCreateIdAsync(new DimensionBag([dimValue]), CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: setId,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 1.0m }),

                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: setId,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 2.0m }),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert: SliceLast returns the latest version (amount=2)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            var asOfUtc = DateTime.UtcNow.AddMinutes(1);
            var record = await read.SliceLastAsync(
                registerId,
                dimensions: [dimValue],
                asOfUtc: asOfUtc,
                recorderDocumentId: null,
                includeDeleted: false,
                ct: CancellationToken.None);

            record.Should().NotBeNull();
            record!.IsDeleted.Should().BeFalse();
            record.Values.Should().ContainKey("amount");
            record.Values["amount"].Should().Be(2.0m);
        }
    }

    [Fact]
    public async Task SliceLast_HidesTombstone_ByDefault_AndCanIncludeDeleted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_TOMBSTONE";
        var registerId = Guid.Empty;

        var dimId = DeterministicGuid.Create("Dimension|building");
        var valueId = DeterministicGuid.Create("Building|B");
        var dimValue = new DimensionValue(dimId, valueId);

        // Arrange
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "SliceLast Tombstone Test",
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
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true),
                ],
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var setId = await dimSets.GetOrCreateIdAsync(new DimensionBag([dimValue]), CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: setId,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 10.0m }),

                    // Tombstone (delete) for the same key
                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: setId,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?>(),
                        IsDeleted: true),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            var hidden = await read.SliceLastAsync(
                registerId,
                dimensions: [dimValue],
                asOfUtc: asOfUtc,
                includeDeleted: false,
                ct: CancellationToken.None);

            hidden.Should().BeNull("tombstone must hide the latest record by default");

            var included = await read.SliceLastAsync(
                registerId,
                dimensions: [dimValue],
                asOfUtc: asOfUtc,
                includeDeleted: true,
                ct: CancellationToken.None);

            included.Should().NotBeNull();
            included!.IsDeleted.Should().BeTrue();
        }
    }
}
