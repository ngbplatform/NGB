using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(RegistersPostgresCollection.Name)]
public sealed class ReferenceRegisterTemporalReadContractTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime AllRecordedVersions = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

    [Theory]
    [InlineData(ReferenceRegisterRecordMode.Independent)]
    [InlineData(ReferenceRegisterRecordMode.SubordinateToRecorder)]
    public async Task BackdatedCorrection_SeparatesEffectiveDateFromKnowledgeAtTheTime(
        ReferenceRegisterRecordMode mode)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var effective = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var requestedDate = effective.AddDays(5);
        var (registerId, recorderId) = await CreateRegisterAsync(host, mode);

        await AppendAsync(host, registerId, recorderId, effective, 100);
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();
        var runtime = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        var original = await reader.SliceLastForEffectiveMomentAsync(
            registerId, Guid.Empty, requestedDate, AllRecordedVersions, recorderId);
        original.Should().NotBeNull();

        await AppendAsync(host, registerId, recorderId, effective, 90);

        var knownBeforeCorrection = await reader.SliceLastForEffectiveMomentAsync(
            registerId, Guid.Empty, requestedDate, original!.RecordedAtUtc, recorderId);
        var knownAfterCorrection = await reader.SliceLastForEffectiveMomentAsync(
            registerId, Guid.Empty, requestedDate, AllRecordedVersions, recorderId);
        var ordinaryHistoricalRead = await runtime.SliceLastByDimensionSetIdAsync(
            registerId, Guid.Empty, requestedDate, recorderId);
        var ordinaryReadBeforeCorrection = await runtime.SliceLastByDimensionSetIdAsync(
            registerId, Guid.Empty, original.RecordedAtUtc, recorderId);

        knownBeforeCorrection!.Values["amount"].Should().Be(100);
        knownAfterCorrection!.Values["amount"].Should().Be(90);
        knownAfterCorrection.RecordedAtUtc.Should().BeAfter(original.RecordedAtUtc);
        ordinaryHistoricalRead.Should().BeNull("both versions were recorded after the requested historical date");
        ordinaryReadBeforeCorrection!.Values["amount"].Should().Be(100);
    }

    [Theory]
    [InlineData(ReferenceRegisterRecordMode.Independent)]
    [InlineData(ReferenceRegisterRecordMode.SubordinateToRecorder)]
    public async Task MonthlyRegister_PreservesTwoEffectiveMomentsWithinOneBucket(
        ReferenceRegisterRecordMode mode)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var firstPeriod = new DateTime(2026, 1, 10, 14, 35, 0, DateTimeKind.Utc);
        var secondPeriod = firstPeriod.AddDays(10);
        var (registerId, recorderId) = await CreateRegisterAsync(host, mode);
        await AppendAsync(host, registerId, recorderId, firstPeriod, 100);
        await AppendAsync(host, registerId, recorderId, secondPeriod, 120);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();
        var beforeFirst = await reader.SliceLastForEffectiveMomentAsync(
            registerId, Guid.Empty, firstPeriod.AddTicks(-10), AllRecordedVersions, recorderId);
        var atFirst = await reader.SliceLastForEffectiveMomentAsync(
            registerId, Guid.Empty, firstPeriod, AllRecordedVersions, recorderId);
        var beforeSecond = await reader.SliceLastForEffectiveMomentAsync(
            registerId, Guid.Empty, secondPeriod.AddTicks(-10), AllRecordedVersions, recorderId);
        var atSecond = await reader.SliceLastForEffectiveMomentAsync(
            registerId, Guid.Empty, secondPeriod, AllRecordedVersions, recorderId);
        var history = await reader.ListKeyHistoryAsync(
            registerId, Guid.Empty, AllRecordedVersions, firstPeriod, recorderId);

        beforeFirst.Should().BeNull();
        atFirst!.Values["amount"].Should().Be(100);
        beforeSecond!.Values["amount"].Should().Be(100);
        atSecond!.Values["amount"].Should().Be(120);
        atFirst.PeriodUtc.Should().Be(firstPeriod);
        atSecond.PeriodUtc.Should().Be(secondPeriod);
        atFirst.PeriodBucketUtc.Should().Be(atSecond.PeriodBucketUtc);
        history.Should().HaveCount(2);
        history.Select(row => row.PeriodUtc).Should().BeEquivalentTo([firstPeriod, secondPeriod]);
    }

    private static async Task<(Guid RegisterId, Guid? RecorderId)> CreateRegisterAsync(
        IHost host,
        ReferenceRegisterRecordMode mode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        var registerId = await management.UpsertAsync(
            "temporal_read_contract", "Temporal read contract", ReferenceRegisterPeriodicity.Month, mode);
        await management.ReplaceFieldsAsync(registerId,
            [new ReferenceRegisterFieldDefinition("amount", "Amount", 1, ColumnType.Int32, false)]);

        Guid? recorderId = null;
        if (mode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            recorderId = await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>()
                .CreateDraftAsync("test_doc", "TEMPORAL-READ", DateTime.UtcNow);
        }

        return (registerId, recorderId);
    }

    private static async Task AppendAsync(
        IHost host,
        Guid registerId,
        Guid? recorderId,
        DateTime period,
        int amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var values = new Dictionary<string, object?> { ["amount"] = amount };
        if (recorderId is null)
        {
            await scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>()
                .UpsertAsync(registerId, [], period, values, Guid.NewGuid());
            return;
        }

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>().AppendAsync(registerId,
            [new ReferenceRegisterRecordWrite(Guid.Empty, period, recorderId, values)]);
        await uow.CommitAsync();
    }
}
