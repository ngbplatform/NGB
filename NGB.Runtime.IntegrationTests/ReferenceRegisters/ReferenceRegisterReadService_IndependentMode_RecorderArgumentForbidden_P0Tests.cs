using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterReadService_IndependentMode_RecorderArgumentForbidden_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLast_Throws_WhenRecorderDocumentIdProvided_ForIndependentRegister()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_RECORDER_FORBIDDEN_SL";
        var recorderId = DeterministicGuid.Create("Doc|RECORDER");

        // Arrange: Independent (recorder is forbidden)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            await mgmt.UpsertAsync(
                code,
                name: "Recorder forbidden (SliceLast)",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);
        }

        // Act / Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            Func<Task> act = async () =>
                await read.SliceLastByDimensionSetIdAsync(
                    ReferenceRegisterId.FromCode(code),
                    dimensionSetId: Guid.Empty,
                    asOfUtc: DateTime.UtcNow,
                    recorderDocumentId: recorderId,
                    includeDeleted: false,
                    ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
            ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
            ex.Which.AssertReason("recorder_forbidden");
        }
    }

    [Fact]
    public async Task SliceLastAll_Throws_WhenRecorderDocumentIdProvided_ForIndependentRegister()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_RECORDER_FORBIDDEN_SLA";
        var recorderId = DeterministicGuid.Create("Doc|RECORDER");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            await mgmt.UpsertAsync(
                code,
                name: "Recorder forbidden (SliceLastAll)",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            Func<Task> act = async () =>
                await read.SliceLastAllAsync(
                    ReferenceRegisterId.FromCode(code),
                    asOfUtc: DateTime.UtcNow,
                    recorderDocumentId: recorderId,
                    afterDimensionSetId: null,
                    limit: 10,
                    includeDeleted: false,
                    ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
            ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
            ex.Which.AssertReason("recorder_forbidden");
        }
    }

    [Fact]
    public async Task SliceLastAllFiltered_Throws_WhenRecorderDocumentIdProvided_ForIndependentRegister()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_RECORDER_FORBIDDEN_SLF";
        var recorderId = DeterministicGuid.Create("Doc|RECORDER");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
            await mgmt.UpsertAsync(
                code,
                name: "Recorder forbidden (SliceLastAllFiltered)",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            // Any non-empty dimension set filter; will be validated and passed through to persistence.
            var dimId = DeterministicGuid.Create("Dimension|building");
            var valueId = DeterministicGuid.Create("Building|A");

            Func<Task> act = async () =>
                await read.SliceLastAllFilteredAsync(
                    ReferenceRegisterId.FromCode(code),
                    asOfUtc: DateTime.UtcNow,
                    requiredDimensions: [new DimensionValue(dimId, valueId)],
                    recorderDocumentId: recorderId,
                    afterDimensionSetId: null,
                    limit: 10,
                    includeDeleted: false,
                    ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
            ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
            ex.Which.AssertReason("recorder_forbidden");
        }
    }
}
