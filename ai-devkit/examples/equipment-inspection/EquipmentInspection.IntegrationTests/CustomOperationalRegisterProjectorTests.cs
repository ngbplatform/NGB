using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class CustomOperationalRegisterProjectorTests
{
    [Fact]
    public async Task Registered_projector_owns_its_month_transaction_and_failure_preserves_the_previous_projection()
    {
        var probe = new ProjectionProbe();
        await using var host = new InspectionHost(configureServices: services =>
            services.AddScoped<IOperationalRegisterMonthProjector>(provider =>
                new UsageProjector(provider.GetRequiredService<IUnitOfWork>(),
                    provider.GetRequiredService<IOperationalRegisterDefaultMonthProjector>(), probe)));
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Custom projection bench")));
        var first = await PostUsageAsync(admin, equipment.Id, 2m);
        var register = OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode);

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var record = (await services.GetRequiredService<IDocumentRepository>().GetAsync(first.Id))!;
        var month = new DateOnly(record.DateUtc.Year, record.DateUtc.Month, 1);
        var endpoint = services.GetRequiredService<IOperationalRegisterAdminEndpoint>();
        Assert.Equal(1, await endpoint.FinalizeRegisterDirtyAsync(register, 1));
        Assert.Equal(1, probe.Calls);
        Assert.Equal(register, probe.LastContext!.RegisterId);
        Assert.Equal(EquipmentUsageDefinitions.RegisterCode, probe.LastContext.RegisterCodeNorm);
        Assert.Equal(month, probe.LastContext.PeriodMonth);
        Assert.Equal(DateTimeKind.Utc, probe.LastContext.NowUtc.Kind);
        await AssertProjectionAsync(services, register, month, 2m);
        Assert.Equal("Finalized", (await endpoint.GetFinalizationAsync(register, month))!.Status);
        Assert.Equal(0, await endpoint.FinalizeRegisterDirtyAsync(register, 1));
        Assert.Equal(1, probe.Calls);

        var management = services.GetRequiredService<IOperationalRegisterManagementService>();
        var other = await management.UpsertAsync("example.other-projection", "Default projection");
        await management.ReplaceResourcesAsync(other, [new OperationalRegisterResourceDefinition("quantity", "Quantity", 1)]);
        await endpoint.EnsurePhysicalSchemaByIdAsync(other);
        await endpoint.MarkFinalizationDirtyAsync(other, month);
        Assert.Equal(1, await endpoint.FinalizeRegisterDirtyAsync(other, 1));
        Assert.Equal("Finalized", (await endpoint.GetFinalizationAsync(other, month))!.Status);
        Assert.Equal(1, probe.Calls);

        await PostUsageAsync(admin, equipment.Id, 1.5m);
        probe.FailAfterWrite = true;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            endpoint.FinalizeRegisterDirtyAsync(register, 1));
        Assert.Equal("Deliberate failure after writing the monthly projection.", error.Message);
        Assert.Equal(2, probe.Calls);
        Assert.False(services.GetRequiredService<IUnitOfWork>().HasActiveTransaction);
        Assert.Equal("Dirty", (await endpoint.GetFinalizationAsync(register, month))!.Status);
        await AssertProjectionAsync(services, register, month, 2m);

        probe.FailAfterWrite = false;
        Assert.Equal(1, await endpoint.FinalizeRegisterDirtyAsync(register, 1));
        Assert.Equal(3, probe.Calls);
        await AssertProjectionAsync(services, register, month, 3.5m);
        Assert.Equal("Finalized", (await endpoint.GetFinalizationAsync(register, month))!.Status);

        var path = "/api/documents/" + EquipmentUsageDefinitions.DocumentCode + "/" + first.Id;
        var state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var unposted = await ExecuteActionAsync(admin, path, "unpost", state.DocumentVersion, "custom-projector-unpost");
        await EnsureSuccessAsync(unposted);
        Assert.Equal(1, await endpoint.FinalizeRegisterDirtyAsync(register, 1));
        Assert.Equal(4, probe.Calls);
        await AssertProjectionAsync(services, register, month, 1.5m);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" EXAMPLE.EQUIPMENT-HOURS ")]
    public async Task Empty_or_duplicate_normalized_projector_codes_fail_before_execution(string additionalCode)
    {
        var probe = new ProjectionProbe();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddScoped<IOperationalRegisterMonthProjector>(provider =>
                new UsageProjector(provider.GetRequiredService<IUnitOfWork>(),
                    provider.GetRequiredService<IOperationalRegisterDefaultMonthProjector>(), probe));
            services.AddSingleton<IOperationalRegisterMonthProjector>(new InvalidProjector(additionalCode));
        });
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();

        Assert.Throws<NgbConfigurationViolationException>(() =>
            scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>());
        Assert.Equal(0, probe.Calls);
        Assert.False(scope.ServiceProvider.GetRequiredService<IUnitOfWork>().HasActiveTransaction);
    }

    private static async Task<DocumentDto> PostUsageAsync(HttpClient admin, Guid equipment, decimal hours)
    {
        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentUsageDefinitions.DocumentCode,
            Payload(("equipment_id", equipment), ("hours", hours)));
        var path = "/api/documents/" + EquipmentUsageDefinitions.DocumentCode + "/" + draft.Id;
        var state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var posted = await ExecutePostAsync(admin, path, state.DocumentVersion, draft.Id.ToString());
        await EnsureSuccessAsync(posted);
        return draft;
    }

    private static async Task AssertProjectionAsync(IServiceProvider services, Guid register, DateOnly month, decimal hours)
    {
        var turnovers = services.GetRequiredService<IOperationalRegisterTurnoversReader>();
        var balances = services.GetRequiredService<IOperationalRegisterBalancesReader>();
        Assert.Equal(hours, Assert.Single(await turnovers.GetPageByMonthsAsync(register, month, month, limit: 2)).Values["hours"]);
        Assert.Equal(hours, Assert.Single(await balances.GetPageByMonthsAsync(register, month, month, limit: 2)).Values["hours"]);
    }

    private sealed class ProjectionProbe
    {
        public int Calls { get; set; }
        public bool FailAfterWrite { get; set; }
        public OperationalRegisterMonthProjectionContext? LastContext { get; set; }
    }

    private sealed class UsageProjector(
        IUnitOfWork unitOfWork,
        IOperationalRegisterDefaultMonthProjector standard,
        ProjectionProbe probe) : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => " " + EquipmentUsageDefinitions.RegisterCode.ToUpperInvariant() + " ";

        public async Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        {
            Assert.True(unitOfWork.HasActiveTransaction);
            probe.Calls++;
            probe.LastContext = context;
            await standard.RebuildMonthAsync(context, ct);
            if (probe.FailAfterWrite)
            {
                throw new InvalidOperationException("Deliberate failure after writing the monthly projection.");
            }
        }
    }

    private sealed class InvalidProjector(string code) : IOperationalRegisterMonthProjector
    {
        public string RegisterCodeNorm => code;

        public Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Invalid projector registration must never execute.");
        }
    }
}
