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
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterReadService_SliceLastAll_SubordinateAndPagination_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SliceLastAll_SubordinateToRecorder_RequiresRecorderId_AndFiltersByRecorder()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_LAST_ALL_SUB";
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
                name: "SliceLastAll Subordinate Test",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
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

        Guid recorder1;
        Guid recorder2;

        // Arrange: recorder documents (FK target)
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            recorder1 = await drafts.CreateDraftAsync(
                typeCode: "it_doc_a",
                number: "RR-REC-1",
                dateUtc: DateTime.UtcNow,
                manageTransaction: true,
                ct: CancellationToken.None);

            recorder2 = await drafts.CreateDraftAsync(
                typeCode: "it_doc_a",
                number: "RR-REC-2",
                dateUtc: DateTime.UtcNow,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        Guid aSetId;
        Guid bSetId;

        // Act: append versions for two recorders
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
                        RecorderDocumentId: recorder1,
                        Values: new Dictionary<string, object?> { ["amount"] = 1.0m }),

                    // Newer version for the same key (recorder1 + dim A)
                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: aSetId,
                        PeriodUtc: null,
                        RecorderDocumentId: recorder1,
                        Values: new Dictionary<string, object?> { ["amount"] = 2.0m }),

                    // Same DimensionSetId but different recorder
                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: aSetId,
                        PeriodUtc: null,
                        RecorderDocumentId: recorder2,
                        Values: new Dictionary<string, object?> { ["amount"] = 100.0m }),

                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: bSetId,
                        PeriodUtc: null,
                        RecorderDocumentId: recorder1,
                        Values: new Dictionary<string, object?> { ["amount"] = 3.0m }),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            var act = () => read.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId: null,
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
            ex.Which.AssertNgbError(ReferenceRegisterRecordsValidationException.Code, "registerId", "reason");
            ex.Which.AssertReason("recorder_required");

            var r1 = await read.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId: recorder1,
                afterDimensionSetId: null,
                limit: 100,
                includeDeleted: false,
                ct: CancellationToken.None);

            r1.Should().HaveCount(2);
            r1.All(x => x.RecorderDocumentId == recorder1).Should().BeTrue();

            var a1 = r1.Single(x => x.DimensionSetId == aSetId);
            a1.Values["amount"].Should().Be(2.0m);

            var b1 = r1.Single(x => x.DimensionSetId == bSetId);
            b1.Values["amount"].Should().Be(3.0m);
        }
    }

    [Fact]
    public async Task SliceLastAll_PaginatesByDimensionSetId_KeysetPagination()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SLICE_LAST_ALL_PG";
        var registerId = Guid.Empty;

        var dimId = DeterministicGuid.Create("Dimension|building");
        var v1 = new DimensionValue(dimId, DeterministicGuid.Create("Building|1"));
        var v2 = new DimensionValue(dimId, DeterministicGuid.Create("Building|2"));
        var v3 = new DimensionValue(dimId, DeterministicGuid.Create("Building|3"));

        // Arrange: metadata
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "SliceLastAll Pagination",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
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

        Guid recorder;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

            recorder = await drafts.CreateDraftAsync(
                typeCode: "it_doc_a",
                number: "RR-PAGE-1",
                dateUtc: DateTime.UtcNow,
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        Guid s1;
        Guid s2;
        Guid s3;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            s1 = await dimSets.GetOrCreateIdAsync(new DimensionBag([v1]), CancellationToken.None);
            s2 = await dimSets.GetOrCreateIdAsync(new DimensionBag([v2]), CancellationToken.None);
            s3 = await dimSets.GetOrCreateIdAsync(new DimensionBag([v3]), CancellationToken.None);

            await store.AppendAsync(
                registerId,
                [
                    new ReferenceRegisterRecordWrite(s1, null, recorder, new Dictionary<string, object?> { ["amount"] = 1.0m }),
                    new ReferenceRegisterRecordWrite(s2, null, recorder, new Dictionary<string, object?> { ["amount"] = 2.0m }),
                    new ReferenceRegisterRecordWrite(s3, null, recorder, new Dictionary<string, object?> { ["amount"] = 3.0m }),
                ],
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfUtc = DateTime.UtcNow.AddMinutes(1);

            var page1 = await read.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId: recorder,
                afterDimensionSetId: null,
                limit: 2,
                includeDeleted: false,
                ct: CancellationToken.None);

            page1.Should().HaveCount(2);
            page1.Select(x => x.DimensionSetId).Should().BeInAscendingOrder();

            var after = page1.Last().DimensionSetId;

            var page2 = await read.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId: recorder,
                afterDimensionSetId: after,
                limit: 2,
                includeDeleted: false,
                ct: CancellationToken.None);

            page2.Should().HaveCount(1);
            page2.Single().DimensionSetId.CompareTo(after).Should().BeGreaterThan(0);

            var all = page1.Concat(page2).ToArray();
            all.Select(x => x.DimensionSetId).Distinct().Should().HaveCount(3);
            all.Select(x => x.Values["amount"]).Should().BeEquivalentTo(new object?[] { 1.0m, 2.0m, 3.0m });
        }
    }
}
