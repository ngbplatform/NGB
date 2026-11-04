using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class OperationalRegisterAdministrationTests
{
    [Fact]
    public async Task Physical_health_and_targeted_ensure_preserve_unrelated_registers()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var management = services.GetRequiredService<IOperationalRegisterManagementService>();
        var endpoint = services.GetRequiredService<IOperationalRegisterAdminEndpoint>();
        var first = await management.UpsertAsync("example.physical-first", "First physical probe");
        var second = await management.UpsertAsync("example.physical-second", "Second physical probe");
        foreach (var id in new[] { first, second })
            await management.ReplaceResourcesAsync(id, [new OperationalRegisterResourceDefinition("quantity", "Quantity", 1)]);

        var details = await endpoint.GetDetailsByCodeAsync("example.physical-first");
        Assert.Equal(first, details!.Register.RegisterId);
        Assert.Equal("quantity", Assert.Single(details.Resources).ColumnCode);
        Assert.Empty(details.DimensionRules);
        Assert.Contains(await endpoint.GetListAsync(), item => item.Register.RegisterId == first && item.ResourcesCount == 1);
        Assert.False((await endpoint.GetPhysicalSchemaHealthByIdAsync(first))!.IsOk);
        Assert.False((await endpoint.GetPhysicalSchemaHealthByIdAsync(second))!.IsOk);
        Assert.Null(await endpoint.GetDetailsByIdAsync(Guid.CreateVersion7()));
        Assert.Null(await endpoint.EnsurePhysicalSchemaByIdAsync(Guid.CreateVersion7()));

        var maintenance = services.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
        await maintenance.EnsurePhysicalSchemasByIdsAsync([first, first, Guid.Empty, Guid.CreateVersion7()]);
        var healthy = (await endpoint.GetPhysicalSchemaHealthByIdAsync(first))!;
        Assert.True(healthy.IsOk);
        Assert.True(healthy.Movements.HasAppendOnlyGuard);
        Assert.False((await endpoint.GetPhysicalSchemaHealthByIdAsync(second))!.IsOk);
        Assert.True((await endpoint.EnsurePhysicalSchemaByIdAsync(first))!.IsOk);
        var all = await endpoint.EnsurePhysicalSchemaForAllAsync();
        Assert.Equal(all.TotalCount, all.OkCount);
        Assert.All(all.Items, item => Assert.True(item.IsOk));
        Assert.False((await endpoint.GetDetailsByIdAsync(first))!.Register.HasMovements);
        Assert.False((await endpoint.GetDetailsByIdAsync(second))!.Register.HasMovements);
    }

    [Fact]
    public async Task Finalization_admin_uses_the_default_projector_for_new_registers_and_bounds_reads()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();
        var register = OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode);
        var month = new DateOnly(2026, 5, 1);
        Assert.Null(await endpoint.GetFinalizationAsync(register, month));
        await endpoint.MarkFinalizationDirtyAsync(register, month);
        Assert.Equal("Dirty", (await endpoint.GetFinalizationAsync(register, month))!.Status);
        Assert.Equal(register, Assert.Single(await endpoint.GetDirtyFinalizationsByIdAsync(register, 1)).RegisterId);
        Assert.Contains(await endpoint.GetDirtyFinalizationsAcrossAllAsync(1), item => item.RegisterId == register);
        Assert.Equal(1, await endpoint.FinalizeRegisterDirtyAsync(register, 1));
        Assert.Equal("Finalized", (await endpoint.GetFinalizationAsync(register, month))!.Status);
        Assert.Empty(await endpoint.GetDirtyFinalizationsByIdAsync(register));
        Assert.Equal(0, await endpoint.FinalizeDirtyAsync(1));

        var management = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var added = await management.UpsertAsync("example.default-projection", "Default projection register");
        await management.ReplaceResourcesAsync(added, [new OperationalRegisterResourceDefinition("quantity", "Quantity", 1)]);
        await endpoint.EnsurePhysicalSchemaByIdAsync(added);
        await endpoint.MarkFinalizationDirtyAsync(added, month);
        Assert.Equal(1, await endpoint.FinalizeRegisterDirtyAsync(added, 1));
        Assert.Equal("Finalized", (await endpoint.GetFinalizationAsync(added, month))!.Status);
        Assert.Empty(await endpoint.GetBlockedFinalizationsByIdAsync(added));
        Assert.Empty(await endpoint.GetBlockedFinalizationsAcrossAllAsync(1));

        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => endpoint.GetDirtyFinalizationsAcrossAllAsync(0));
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => endpoint.GetBlockedFinalizationsAcrossAllAsync(int.MaxValue));
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => endpoint.FinalizeDirtyAsync(0));
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() => endpoint.FinalizeRegisterDirtyAsync(register, int.MaxValue));
    }

    [Fact]
    public async Task A_host_without_a_default_projector_reports_blocked_months_until_explicitly_marked_dirty()
    {
        await using var host = new InspectionHost(configureServices: services =>
            services.RemoveAll<IOperationalRegisterDefaultMonthProjector>());
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();
        var management = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var register = await management.UpsertAsync("example.unprojected", "Unprojected register");
        await management.ReplaceResourcesAsync(register, [new OperationalRegisterResourceDefinition("quantity", "Quantity", 1)]);
        await endpoint.EnsurePhysicalSchemaByIdAsync(register);
        var month = new DateOnly(2026, 5, 1);
        await endpoint.MarkFinalizationDirtyAsync(register, month);
        Assert.Equal(0, await endpoint.FinalizeRegisterDirtyAsync(register, 1));
        Assert.Equal("BlockedNoProjector", (await endpoint.GetFinalizationAsync(register, month))!.Status);
        Assert.Equal(register, Assert.Single(await endpoint.GetBlockedFinalizationsByIdAsync(register, 1)).RegisterId);
        Assert.Contains(await endpoint.GetBlockedFinalizationsAcrossAllAsync(1), item => item.RegisterId == register);
        Assert.Equal(0, await endpoint.FinalizeDirtyAsync(1));
        await endpoint.MarkFinalizationDirtyAsync(register, month);
        Assert.Empty(await endpoint.GetBlockedFinalizationsByIdAsync(register));
        Assert.Equal("Dirty", (await endpoint.GetFinalizationAsync(register, month))!.Status);
    }

    [Fact]
    public async Task Resource_net_reads_keep_dimension_sets_separate_and_reconcile_storno_without_finalization()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var first = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Net first")));
        var second = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Net second")));
        var documents = new List<string>();
        foreach (var (equipment, hours) in new[] { (first.Id, 2.5m), (first.Id, 1.5m), (second.Id, 5m) })
        {
            var draft = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentUsageDefinitions.DocumentCode,
                Payload(("equipment_id", equipment), ("hours", hours)));
            var path = "/api/documents/" + EquipmentUsageDefinitions.DocumentCode + "/" + draft.Id;
            var state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
            using var posted = await ExecutePostAsync(admin, path, state.DocumentVersion, draft.Id.ToString());
            await EnsureSuccessAsync(posted);
            documents.Add(path);
        }

        await AssertNetsAsync(host, first.Id, second.Id, 4m, 5m);
        var postedState = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(documents[0] + "/editor-state", Json))!;
        using var unposted = await ExecuteActionAsync(admin, documents[0], "unpost", postedState.DocumentVersion, "net-unpost");
        await EnsureSuccessAsync(unposted);
        await AssertNetsAsync(host, first.Id, second.Id, 1.5m, 5m);
    }

    private static async Task AssertNetsAsync(InspectionHost host, Guid first, Guid second, decimal firstNet, decimal secondNet)
    {
        var register = OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode);
        IReadOnlyList<DimensionValue> firstDimensions = [new(EquipmentDefinitions.EquipmentDimensionId, first)];
        IReadOnlyList<DimensionValue> secondDimensions = [new(EquipmentDefinitions.EquipmentDimensionId, second)];
        IReadOnlyList<DimensionValue> missingDimensions = [new(EquipmentDefinitions.EquipmentDimensionId, Guid.CreateVersion7())];
        var firstSet = DeterministicDimensionSetId.FromBag(new DimensionBag(firstDimensions));
        var secondSet = DeterministicDimensionSetId.FromBag(new DimensionBag(secondDimensions));
        var missingSet = DeterministicDimensionSetId.FromBag(new DimensionBag(missingDimensions));
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceNetReader>();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(async ct =>
        {
            Assert.Equal(firstNet, await reader.GetNetByDimensionSetAsync(register, firstSet, "hours", ct));
            var batch = await reader.GetNetByDimensionSetsAsync(register, [firstSet, secondSet, firstSet, missingSet], "hours", ct);
            Assert.Equal(3, batch.Count);
            Assert.Equal(firstNet, batch[firstSet]);
            Assert.Equal(secondNet, batch[secondSet]);
            Assert.Equal(0m, batch[missingSet]);
            Assert.Equal(firstNet, await reader.GetNetByDimensionsAsync(register, firstDimensions, "hours", ct));
            Assert.Equal(0m, await reader.GetNetByDimensionsAsync(register, missingDimensions, "hours", ct));
            IReadOnlyList<IReadOnlyList<DimensionValue>> groups = [secondDimensions, missingDimensions, firstDimensions];
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            Assert.Equal(new[] { secondNet, 0m, firstNet }, await reader.GetNetsByDimensionsAsync(register, groups, "hours", today, ct));
            Assert.All(await reader.GetNetsByDimensionsAsync(register, groups, "hours", today.AddDays(-1), ct), value => Assert.Equal(0m, value));
            Assert.Empty(await reader.GetNetByDimensionSetsAsync(register, [], "hours", ct));
            Assert.Empty(await reader.GetNetsByDimensionsAsync(register, [], "hours", today, ct));
            await Assert.ThrowsAsync<NgbConfigurationViolationException>(() => reader.GetNetByDimensionSetAsync(register, firstSet, "unknown_resource", ct));
            await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => reader.GetNetByDimensionsAsync(register, [], "hours", ct));
        });
    }
}
