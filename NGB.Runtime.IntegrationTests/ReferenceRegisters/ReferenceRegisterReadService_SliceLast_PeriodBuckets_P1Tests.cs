using Dapper;
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
public sealed class ReferenceRegisterReadService_SliceLast_PeriodBuckets_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLast_Monthly_FallsBackToPreviousBucket_WhenCurrentBucketHasOnlyFuturePeriods()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_MONTH";
        var registerId = Guid.Empty;

        var dimId = DeterministicGuid.Create("Dimension|building");
        var valueId = DeterministicGuid.Create("Building|C");
        var dimValue = new DimensionValue(dimId, valueId);

        var now = DateTime.UtcNow;
        var thisMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var prevMonthStart = thisMonthStart.AddMonths(-1);
        var nextMonthStart = thisMonthStart.AddMonths(1);

        var prevPeriodUtc = prevMonthStart.AddDays(10).AddHours(12);
        var nextPeriodUtc = nextMonthStart.AddDays(10).AddHours(12);

        // Arrange: metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "SliceLast Month Bucket Test",
                periodicity: ReferenceRegisterPeriodicity.Month,
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

        // Arrange: insert rows with controlled recorded_at_utc so we can use stable as-of timestamps.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var setId = await dimSets.GetOrCreateIdAsync(new DimensionBag([dimValue]), CancellationToken.None);

            await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            var table = ReferenceRegisterNaming.RecordsTable(code);

            var prevBucketUtc = ReferenceRegisterPeriodBucket.ComputeUtc(prevPeriodUtc, ReferenceRegisterPeriodicity.Month)!.Value;
            var nextBucketUtc = ReferenceRegisterPeriodBucket.ComputeUtc(nextPeriodUtc, ReferenceRegisterPeriodicity.Month)!.Value;

            // We intentionally set recorded_at_utc to stable points (relative to thisMonthStart)
            // to avoid relying on clock timing.
            var insertSql = $"""
                            INSERT INTO {table} (
                                dimension_set_id,
                                period_utc,
                                period_bucket_utc,
                                recorder_document_id,
                                recorded_at_utc,
                                is_deleted,
                                amount
                            )
                            VALUES
                                (@DimensionSetId, @PrevPeriodUtc, @PrevBucketUtc, NULL, @PrevRecordedAtUtc, FALSE, @PrevAmount),
                                (@DimensionSetId, @NextPeriodUtc, @NextBucketUtc, NULL, @NextRecordedAtUtc, FALSE, @NextAmount);
                            """;

            var insertCmd = new CommandDefinition(
                insertSql,
                new
                {
                    DimensionSetId = setId,
                    PrevPeriodUtc = prevPeriodUtc,
                    PrevBucketUtc = prevBucketUtc,
                    PrevRecordedAtUtc = thisMonthStart.AddDays(1),
                    PrevAmount = 1.0m,
                    NextPeriodUtc = nextPeriodUtc,
                    NextBucketUtc = nextBucketUtc,
                    NextRecordedAtUtc = thisMonthStart.AddDays(2),
                    NextAmount = 2.0m
                },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None);

            await uow.Connection.ExecuteAsync(insertCmd);

            // In production the store sets has_records; here we do it explicitly because we inserted via SQL.
            const string markHasRecordsSql = "UPDATE reference_registers SET has_records = TRUE WHERE register_id = @Id;";

            var markCmd = new CommandDefinition(
                markHasRecordsSql,
                new { Id = registerId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None);

            await uow.Connection.ExecuteAsync(markCmd);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert: as-of early next month should ignore the future (next-month) record and fall back to previous month.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            var asOfEarlyNextMonthUtc = nextMonthStart.AddDays(5); // next bucket, but before nextPeriodUtc
            var r1 = await read.SliceLastAsync(
                registerId,
                dimensions: [dimValue],
                asOfUtc: asOfEarlyNextMonthUtc,
                includeDeleted: false,
                ct: CancellationToken.None);

            r1.Should().NotBeNull();
            r1!.PeriodUtc.Should().Be(prevPeriodUtc);
            r1.Values["amount"].Should().Be(1.0m);

            var asOfLateNextMonthUtc = nextMonthStart.AddDays(15);
            var r2 = await read.SliceLastAsync(
                registerId,
                dimensions: [dimValue],
                asOfUtc: asOfLateNextMonthUtc,
                includeDeleted: false,
                ct: CancellationToken.None);

            r2.Should().NotBeNull();
            r2!.PeriodUtc.Should().Be(nextPeriodUtc);
            r2.Values["amount"].Should().Be(2.0m);
        }
    }
}
