using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterRecordsReader_IndependentMode_RecorderArgumentForbidden_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLast_WhenIndependent_RecorderDocumentIdArgumentIsForbidden()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = await CreateIndependentNonPeriodicRegisterAsync(host, "RR_READER_REC_FORBID_SLICE_LAST");

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();

        var act = async () =>
        {
            await reader.SliceLastAsync(
                registerId,
                dimensionSetId: Guid.Empty,
                asOfUtc: DateTime.UtcNow,
                recorderDocumentId: Guid.CreateVersion7(),
                ct: CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("recorder_forbidden");
    }

    [Fact]
    public async Task SliceLastAll_WhenIndependent_RecorderDocumentIdArgumentIsForbidden()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = await CreateIndependentNonPeriodicRegisterAsync(host, "RR_READER_REC_FORBID_SLICE_LAST_ALL");

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();

        var act = async () =>
        {
            await reader.SliceLastAllAsync(
                registerId,
                asOfUtc: DateTime.UtcNow,
                recorderDocumentId: Guid.CreateVersion7(),
                afterDimensionSetId: null,
                limit: 10,
                ct: CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("recorder_forbidden");
    }

    [Fact]
    public async Task SliceLastAllFilteredByDimensions_WhenIndependent_RecorderDocumentIdArgumentIsForbidden()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = await CreateIndependentNonPeriodicRegisterAsync(host, "RR_READER_REC_FORBID_SLICE_LAST_ALL_FILTERED");

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();

        var dimId = DeterministicGuid.Create("Dimension|building");
        var valueId = DeterministicGuid.Create("Building|X");
        var dv = new DimensionValue(dimId, valueId);

        var act = async () =>
        {
            await reader.SliceLastAllFilteredByDimensionsAsync(
                registerId,
                asOfUtc: DateTime.UtcNow,
                requiredDimensions: [dv],
                recorderDocumentId: Guid.CreateVersion7(),
                afterDimensionSetId: null,
                limit: 10,
                ct: CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
        ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
        ex.Which.AssertReason("recorder_forbidden");
    }

    private static async Task<Guid> CreateIndependentNonPeriodicRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var registerId = await svc.UpsertAsync(
            code,
            name: code,
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct: CancellationToken.None);

        var dimId = DeterministicGuid.Create("Dimension|building");

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

        return registerId;
    }
}
