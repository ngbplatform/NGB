using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterReadService_KeyHistory_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetKeyHistory_NonPeriodic_OrdersAndPaginates_AndHonorsIncludeDeleted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_KEY_HISTORY_NONPERIODIC";
        var registerId = Guid.Empty;

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var bA = new DimensionValue(buildingDimId, DeterministicGuid.Create("Building|A"));

        // Arrange: register metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "KeyHistory NonPeriodic Test",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(
                registerId,
                [new ReferenceRegisterDimensionRule(buildingDimId, "building", Ordinal: 10, IsRequired: true)],
                ct: CancellationToken.None);

            await mgmt.ReplaceFieldsAsync(
                registerId,
                [new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true)],
                ct: CancellationToken.None);
        }

        Guid setA;

        // Act: append 3 versions for a single key (including tombstone)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            setA = await dimSets.GetOrCreateIdAsync(new DimensionBag([bA]), CancellationToken.None);

            await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(setA, PeriodUtc: null, RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 1.0m }),

                    new ReferenceRegisterRecordWrite(setA, PeriodUtc: null, RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 2.0m }),

                    new ReferenceRegisterRecordWrite(setA, PeriodUtc: null, RecorderDocumentId: null,
                        Values: new Dictionary<string, object?>(), IsDeleted: true),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            // Default: tombstone hidden
            var visible = await read.GetKeyHistoryByDimensionSetIdAsync(
                registerId,
                setA,
                asOfUtc,
                periodUtc: null,
                recorderDocumentId: null,
                beforeRecordedAtUtc: null,
                beforeRecordId: null,
                limit: 100,
                includeDeleted: false,
                ct: CancellationToken.None);

            visible.Should().HaveCount(2);
            visible.Select(x => x.RecordId).Should().BeInDescendingOrder();

            visible[0].IsDeleted.Should().BeFalse();
            ((decimal)visible[0].Values["amount"]!).Should().Be(2.0m);
            ((decimal)visible[1].Values["amount"]!).Should().Be(1.0m);

            // includeDeleted: tombstone should be first
            var full = await read.GetKeyHistoryByDimensionSetIdAsync(
                registerId,
                setA,
                asOfUtc,
                periodUtc: null,
                recorderDocumentId: null,
                beforeRecordedAtUtc: null,
                beforeRecordId: null,
                limit: 100,
                includeDeleted: true,
                ct: CancellationToken.None);

            full.Should().HaveCount(3);
            full.Select(x => x.RecordId).Should().BeInDescendingOrder();
            full[0].IsDeleted.Should().BeTrue();

            // Pagination (version-based): first page (2 newest), then request older
            var page1 = await read.GetKeyHistoryByDimensionSetIdAsync(
                registerId,
                setA,
                asOfUtc,
                periodUtc: null,
                recorderDocumentId: null,
                beforeRecordedAtUtc: null,
                beforeRecordId: null,
                limit: 2,
                includeDeleted: true,
                ct: CancellationToken.None);

            page1.Should().HaveCount(2);

            var cursor = page1.Last();

            var page2 = await read.GetKeyHistoryByDimensionSetIdAsync(
                registerId,
                setA,
                asOfUtc,
                periodUtc: null,
                recorderDocumentId: null,
                beforeRecordedAtUtc: cursor.RecordedAtUtc,
                beforeRecordId: cursor.RecordId,
                limit: 100,
                includeDeleted: true,
                ct: CancellationToken.None);

            page2.Should().HaveCount(1);
            page2[0].IsDeleted.Should().BeFalse();
            ((decimal)page2[0].Values["amount"]!).Should().Be(1.0m);
        }
    }

    [Fact]
    public async Task GetKeyHistory_Monthly_RequiresPeriodUtc_AndFiltersByBucket()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_KEY_HISTORY_MONTHLY";
        var registerId = Guid.Empty;

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var bA = new DimensionValue(buildingDimId, DeterministicGuid.Create("Building|A"));

        // Arrange: register metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "KeyHistory Monthly Test",
                periodicity: ReferenceRegisterPeriodicity.Month,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(
                registerId,
                [new ReferenceRegisterDimensionRule(buildingDimId, "building", Ordinal: 10, IsRequired: true)],
                ct: CancellationToken.None);

            await mgmt.ReplaceFieldsAsync(
                registerId,
                [new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true)],
                ct: CancellationToken.None);
        }

        Guid setA;
        var jan = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act: append versions for the same key in different period buckets
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            setA = await dimSets.GetOrCreateIdAsync(new DimensionBag([bA]), CancellationToken.None);

            await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(setA, PeriodUtc: jan, RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 10.0m }),

                    new ReferenceRegisterRecordWrite(setA, PeriodUtc: jan, RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 20.0m }),

                    new ReferenceRegisterRecordWrite(setA, PeriodUtc: feb, RecorderDocumentId: null,
                        Values: new Dictionary<string, object?> { ["amount"] = 30.0m }),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            // Periodic register must receive periodUtc
            Func<Task> missingPeriod = () => read.GetKeyHistoryByDimensionSetIdAsync(
                registerId,
                setA,
                asOfUtc,
                periodUtc: null,
                recorderDocumentId: null,
                beforeRecordedAtUtc: null,
                beforeRecordId: null,
                limit: 10,
                includeDeleted: true,
                ct: CancellationToken.None);

            var ex = await missingPeriod.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
            ex.Which.Reason.Should().Be("period_required_for_periodic");
            ex.Which.Context.Keys.Should().Contain("periodicity");

            var janHistory = await read.GetKeyHistoryByDimensionSetIdAsync(
                registerId,
                setA,
                asOfUtc,
                periodUtc: jan,
                recorderDocumentId: null,
                beforeRecordedAtUtc: null,
                beforeRecordId: null,
                limit: 100,
                includeDeleted: true,
                ct: CancellationToken.None);

            janHistory.Should().HaveCount(2);
            ((decimal)janHistory[0].Values["amount"]!).Should().Be(20.0m);
            ((decimal)janHistory[1].Values["amount"]!).Should().Be(10.0m);

            var febHistory = await read.GetKeyHistoryByDimensionSetIdAsync(
                registerId,
                setA,
                asOfUtc,
                periodUtc: feb,
                recorderDocumentId: null,
                beforeRecordedAtUtc: null,
                beforeRecordId: null,
                limit: 100,
                includeDeleted: true,
                ct: CancellationToken.None);

            febHistory.Should().HaveCount(1);
            ((decimal)febHistory[0].Values["amount"]!).Should().Be(30.0m);
        }
    }
}
