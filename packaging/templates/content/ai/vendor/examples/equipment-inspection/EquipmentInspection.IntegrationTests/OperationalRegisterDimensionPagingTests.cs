using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class OperationalRegisterDimensionPagingTests
{
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateOnly February = new(2026, 2, 1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Signed_dimension_pages_reconcile_storno_and_snapshot_roll_forward(bool finalizeBaseline)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var first = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Positive first")));
        var negative = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Negative")));
        var second = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Positive second")));
        var zero = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Cancelled")));
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var management = services.GetRequiredService<IOperationalRegisterManagementService>();
        var maintenance = services.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
        var register = await management.UpsertAsync("example.dimension-net-probe", "Signed dimension net probe");
        await management.ReplaceResourcesAsync(register, [new OperationalRegisterResourceDefinition("quantity", "Quantity", 1)]);
        await management.ReplaceDimensionRulesAsync(register,
            [new OperationalRegisterDimensionRule(EquipmentDefinitions.EquipmentDimensionId, "example.equipment", 1, true)]);
        await maintenance.EnsurePhysicalSchemaByIdAsync(register);

        // This reader fixture seeds signed facts through the trusted public applier.
        // It does not bypass or claim acceptance of an interactive document Post rule.
        await AppendAsync(admin, services, register, first.Id, January, 10m);
        await AppendAsync(admin, services, register, second.Id, January, 3m);
        await AppendAsync(admin, services, register, negative.Id, January, -7m);
        var cancelled = await AppendAsync(admin, services, register, zero.Id, January, 5m);
        Assert.Equal(OperationalRegisterWriteResult.Executed,
            await services.GetRequiredService<IOperationalRegisterMovementsApplier>().ApplyMovementsForDocumentAsync(
                register, cancelled, OperationalRegisterWriteOperation.Unpost, []));
        if (finalizeBaseline)
            Assert.Equal(1, await maintenance.FinalizeRegisterDirtyAsync(register));

        await AppendAsync(admin, services, register, first.Id, February, 2m);
        await AppendAsync(admin, services, register, negative.Id, February, 1m);
        var reads = services.GetRequiredService<IOperationalRegisterMovementsQueryReader>();
        var expected = new[]
        {
            new OperationalRegisterDimensionResourceNetRow(first.Id, 12m, "Positive first"),
            new OperationalRegisterDimensionResourceNetRow(second.Id, 3m, "Positive second"),
            new OperationalRegisterDimensionResourceNetRow(negative.Id, -6m, "Negative"),
        };

        await AssertBalancePagesAsync(reads, register, expected);
        var january = await reads.GetResourceNetsByDimensionAsync(register, January, January, null,
            EquipmentDefinitions.EquipmentDimensionId, "quantity");
        Assert.Equal(expected.Select(row => row.ValueId), january.Select(row => row.ValueId));
        Assert.Equal(new[] { 10m, 3m, -7m }, january.Select(row => row.NetAmount));
        for (var offset = 0; offset < january.Count; offset++)
        {
            var page = await reads.GetResourceNetsByDimensionPageAsync(register, January, January, null,
                EquipmentDefinitions.EquipmentDimensionId, "quantity", offset, 1);
            Assert.Equal(january[offset], Assert.Single(page.Rows));
            Assert.Equal(3, page.Total);
            Assert.Equal(13m, page.TotalPositive);
            Assert.Equal(7m, page.TotalNegativeAbsolute);
        }

        var february = await reads.GetResourceNetsByDimensionPageAsync(register, February, February, null,
            EquipmentDefinitions.EquipmentDimensionId, "quantity", 0, 1);
        Assert.Equal(2, february.Total);
        Assert.Equal(3m, february.TotalPositive);
        Assert.Equal(0m, february.TotalNegativeAbsolute);
        Assert.Equal(first.Id, Assert.Single(february.Rows).ValueId);

        Assert.Equal(finalizeBaseline ? 1 : 2, await maintenance.FinalizeRegisterDirtyAsync(register));
        await AssertBalancePagesAsync(reads, register, expected);
        IReadOnlyList<DimensionValue> filter = [new(EquipmentDefinitions.EquipmentDimensionId, negative.Id)];
        var filtered = await reads.GetResourceBalancesByDimensionCursorAsync(register, February, filter,
            EquipmentDefinitions.EquipmentDimensionId, "quantity", null, 1);
        Assert.Equal(expected[2], Assert.Single(filtered.Rows));
        Assert.Equal(1, filtered.Total);
        Assert.Equal(0m, filtered.TotalPositive);
        Assert.Equal(6m, filtered.TotalNegativeAbsolute);
        Assert.False(filtered.HasMore);
        Assert.Empty(await reads.GetResourceBalancesByDimensionAsync(register, January.AddMonths(-1), null,
            EquipmentDefinitions.EquipmentDimensionId, "quantity"));

        await Assert.ThrowsAsync<NgbConfigurationViolationException>(() => reads.GetResourceBalancesByDimensionCursorAsync(
            register, February, null, EquipmentDefinitions.EquipmentDimensionId, "unknown", null, 1));
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => reads.GetResourceNetsByDimensionPageAsync(
            register, February, January, null, EquipmentDefinitions.EquipmentDimensionId, "quantity", 0, 1));
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => reads.GetResourceBalancesByDimensionPageAsync(
            register, February, null, EquipmentDefinitions.EquipmentDimensionId, "quantity", -1, 1));
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => reads.GetResourceBalancesByDimensionCursorAsync(
            register, February, null, EquipmentDefinitions.EquipmentDimensionId, "quantity", null, 0));
    }

    [Fact]
    public async Task Occurrence_and_month_history_use_distinct_ordering_and_preserve_storno_and_dimension_filters()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var first = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "History first")));
        var second = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "History second")));
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var management = services.GetRequiredService<IOperationalRegisterManagementService>();
        var register = await management.UpsertAsync("example.dimension-history-probe", "Dimension history probe");
        await management.ReplaceResourcesAsync(register, [new OperationalRegisterResourceDefinition("quantity", "Quantity", 1)]);
        await management.ReplaceDimensionRulesAsync(register,
            [new OperationalRegisterDimensionRule(EquipmentDefinitions.EquipmentDimensionId, "example.equipment", 1, true)]);
        var reads = services.GetRequiredService<IOperationalRegisterMovementsQueryReader>();
        Assert.Null(await reads.GetMaxPeriodMonthAsync(register));
        Assert.Empty(await reads.GetByMonthsAsync(register, January, February));
        await services.GetRequiredService<IOperationalRegisterAdminMaintenanceService>().EnsurePhysicalSchemaByIdAsync(register);

        // Append out of occurrence order, including three facts at the same instant.
        var february = await AppendAsync(admin, services, register, first.Id, February, 4m);
        var sameTime = await AppendAsync(admin, services, register, second.Id, January.AddDays(19), 2m);
        var earlier = await AppendAsync(admin, services, register, first.Id, January.AddDays(9), 3m);
        var reversed = await AppendAsync(admin, services, register, first.Id, January.AddDays(19), 5m);
        await services.GetRequiredService<IOperationalRegisterMovementsApplier>().ApplyMovementsForDocumentAsync(
            register, reversed, OperationalRegisterWriteOperation.Unpost, []);

        var all = await reads.GetByOccurredAtPageAsync(register, January, February, limit: 5);
        Assert.Equal(5, all.Total);
        Assert.Equal(new[] { earlier, sameTime, reversed, reversed, february }, all.Rows.Select(row => row.DocumentId));
        Assert.Equal(new[] { false, false, false, true, false }, all.Rows.Select(row => row.IsStorno));
        Assert.Equal(new[] { 3m, 2m, 5m, 5m, 4m }, all.Rows.Select(row => row.Values["quantity"]));
        Assert.All(all.Rows, row =>
        {
            var equipment = row.DocumentId == sameTime ? second : first;
            Assert.Equal(new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, equipment.Id), Assert.Single(row.Dimensions));
            Assert.Equal(row.DocumentId == sameTime ? "History second" : "History first",
                row.DimensionValueDisplays[EquipmentDefinitions.EquipmentDimensionId]);
            Assert.Equal(DateTimeKind.Utc, row.OccurredAtUtc.Kind);
        });

        var singleDate = await reads.GetByOccurredAtPageAsync(register, January.AddDays(19), January.AddDays(19), limit: 3);
        Assert.Equal(3, singleDate.Total);
        Assert.Equal(all.Rows.Skip(1).Take(3).Select(row => row.MovementId), singleDate.Rows.Select(row => row.MovementId));
        var offsetPage = await reads.GetByOccurredAtPageAsync(register, January, February, offset: 2, limit: 2);
        Assert.Equal(5, offsetPage.Total);
        Assert.Equal(all.Rows.Skip(2).Take(2).Select(row => row.MovementId), offsetPage.Rows.Select(row => row.MovementId));
        var pastEnd = await reads.GetByOccurredAtPageAsync(register, January, February, offset: 5, limit: 2);
        Assert.Empty(pastEnd.Rows);
        Assert.Equal(5, pastEnd.Total);

        var firstPage = await reads.GetByOccurredAtCursorAsync(register, January, February, limit: 2);
        Assert.Equal(2, firstPage.Count);
        var firstLast = firstPage[^1];
        var cursor = new OperationalRegisterOccurredAtCursor(firstLast.OccurredAtUtc, firstLast.MovementId);
        var observed = firstPage.Select(row => row.MovementId).ToList();
        foreach (var count in new[] { 2, 1 })
        {
            var page = await reads.GetByOccurredAtCursorAsync(register, January, February, cursor: cursor, limit: 2);
            Assert.Equal(count, page.Count);
            observed.AddRange(page.Select(row => row.MovementId));
            var last = page[^1];
            cursor = new OperationalRegisterOccurredAtCursor(last.OccurredAtUtc, last.MovementId);
        }

        Assert.Equal(all.Rows.Select(row => row.MovementId), observed);
        Assert.Empty(await reads.GetByOccurredAtCursorAsync(register, January, February, cursor: cursor, limit: 2));
        IReadOnlyList<DimensionValue> filter = [new(EquipmentDefinitions.EquipmentDimensionId, second.Id)];
        var filtered = await reads.GetByOccurredAtPageAsync(register, January, February, filter, limit: 2);
        Assert.Equal(1, filtered.Total);
        Assert.Equal(sameTime, Assert.Single(filtered.Rows).DocumentId);
        Assert.Equal(sameTime, Assert.Single(await reads.GetByOccurredAtCursorAsync(register, January, February,
            filter, limit: 2)).DocumentId);
        Assert.Empty(await reads.GetByOccurredAtCursorAsync(register, February.AddDays(1), February.AddDays(2), limit: 2));
        var beforeFiltered = new OperationalRegisterOccurredAtCursor(all.Rows[0].OccurredAtUtc, all.Rows[0].MovementId);
        Assert.Equal(sameTime, Assert.Single(await reads.GetByOccurredAtCursorAsync(register, January, February, filter,
            cursor: beforeFiltered, limit: 2)).DocumentId);

        var byMonth = await reads.GetByMonthsAsync(register, January, February, limit: 5);
        Assert.Equal(new[] { february, sameTime, earlier, reversed, reversed }, byMonth.Select(row => row.DocumentId));
        Assert.Equal(byMonth.Skip(2).Take(2).Select(row => row.MovementId),
            (await reads.GetByMonthsAsync(register, January, February, afterMovementId: byMonth[1].MovementId, limit: 2))
            .Select(row => row.MovementId));
        var storno = Assert.Single(await reads.GetByMonthsAsync(register, January, January, documentId: reversed, isStorno: true));
        Assert.True(storno.IsStorno);
        Assert.Equal(reversed, storno.DocumentId);
        Assert.Equal(sameTime, Assert.Single(await reads.GetByMonthsAsync(register, January, February, filter,
            dimensionSetId: all.Rows[1].DimensionSetId, documentId: sameTime, isStorno: false)).DocumentId);
        Assert.Equal(February, await reads.GetMaxPeriodMonthAsync(register));
        Assert.Equal(January, await reads.GetMaxPeriodMonthAsync(register, filter));
        Assert.Equal(January, await reads.GetMaxPeriodMonthAsync(register, dimensionSetId: storno.DimensionSetId,
            documentId: reversed, isStorno: true));
        Assert.Null(await reads.GetMaxPeriodMonthAsync(register, documentId: Guid.CreateVersion7()));

        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => reads.GetByOccurredAtCursorAsync(
            register, January, February, cursor: new OperationalRegisterOccurredAtCursor(all.Rows[0].OccurredAtUtc, 0)));
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => reads.GetByOccurredAtPageAsync(register, February, January));
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => reads.GetByMonthsAsync(register, January, February, limit: 0));
    }

    private static async Task<Guid> AppendAsync(HttpClient admin, IServiceProvider services, Guid register,
        Guid equipment, DateOnly occurredOn, decimal quantity)
    {
        var document = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentDefinitions.InspectionCode,
            Payload(("display", "Dimension paging fixture")));
        var dimensions = services.GetRequiredService<IDimensionSetService>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var dimensionSet = await unitOfWork.ExecuteInUowTransactionAsync(ct => dimensions.GetOrCreateIdAsync(
            new DimensionBag([new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, equipment)]), ct));
        var occurredAt = occurredOn.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        var result = await services.GetRequiredService<IOperationalRegisterMovementsApplier>().ApplyMovementsForDocumentAsync(
            register, document.Id, OperationalRegisterWriteOperation.Post,
            [new OperationalRegisterMovement(document.Id, occurredAt, dimensionSet,
                new Dictionary<string, decimal> { ["quantity"] = quantity })]);
        Assert.Equal(OperationalRegisterWriteResult.Executed, result);
        return document.Id;
    }

    private static async Task AssertBalancePagesAsync(IOperationalRegisterMovementsQueryReader reads, Guid register,
        IReadOnlyList<OperationalRegisterDimensionResourceNetRow> expected)
    {
        var dimension = EquipmentDefinitions.EquipmentDimensionId;
        Assert.Equal(expected, await reads.GetResourceBalancesByDimensionAsync(register, February, null, dimension, "quantity"));
        for (var offset = 0; offset < expected.Count; offset++)
        {
            var page = await reads.GetResourceBalancesByDimensionPageAsync(register, February, null, dimension, "quantity", offset, 1);
            Assert.Equal(expected[offset], Assert.Single(page.Rows));
            Assert.Equal(3, page.Total);
            Assert.Equal(15m, page.TotalPositive);
            Assert.Equal(6m, page.TotalNegativeAbsolute);
        }

        OperationalRegisterDimensionResourceNetCursor? cursor = null;
        for (var offset = 0; offset < expected.Count; offset++)
        {
            var page = await reads.GetResourceBalancesByDimensionCursorAsync(register, February, null, dimension, "quantity", cursor, 1);
            var row = Assert.Single(page.Rows);
            Assert.Equal(expected[offset], row);
            Assert.Equal(3, page.Total);
            Assert.Equal(15m, page.TotalPositive);
            Assert.Equal(6m, page.TotalNegativeAbsolute);
            Assert.Equal(offset < expected.Count - 1, page.HasMore);
            cursor = new OperationalRegisterDimensionResourceNetCursor(row.NetAmount > 0, row.ValueId, offset + 1,
                page.Total, page.TotalPositive, page.TotalNegativeAbsolute);
        }

        var exhausted = await reads.GetResourceBalancesByDimensionCursorAsync(register, February, null, dimension, "quantity", cursor, 1);
        Assert.Empty(exhausted.Rows);
        Assert.False(exhausted.HasMore);
        Assert.Equal(3, exhausted.Total);
        Assert.Equal(15m, exhausted.TotalPositive);
        Assert.Equal(6m, exhausted.TotalNegativeAbsolute);
    }
}
