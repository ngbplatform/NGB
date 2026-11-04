using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.ReferenceRegisters;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class ReferenceRegisterAdministrationTests
{
    [Theory]
    [InlineData(ReferenceRegisterRecordMode.Independent)]
    [InlineData(ReferenceRegisterRecordMode.SubordinateToRecorder)]
    public async Task Physical_health_and_targeted_ensure_repair_only_selected_missing_tables(ReferenceRegisterRecordMode mode)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var registers = services.GetRequiredService<IReferenceRegisterRepository>();
        var fields = services.GetRequiredService<IReferenceRegisterFieldRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var code = "example.reference-health-" + mode.ToString().ToLowerInvariant();
        var first = ReferenceRegisterId.FromCode(code);
        var second = ReferenceRegisterId.FromCode(code + "-second");

        // Seed metadata through public persistence ports to model an incomplete setup.
        // Normal management Upsert ensures physical tables immediately.
        await unitOfWork.ExecuteInUowTransactionAsync(async cancellation =>
        {
            foreach (var (id, registerCode) in new[] { (first, code), (second, code + "-second") })
            {
                await registers.UpsertAsync(new ReferenceRegisterUpsert(id, registerCode, "Reference health probe",
                    ReferenceRegisterPeriodicity.NonPeriodic, mode), DateTime.UtcNow, cancellation);
                await fields.ReplaceAsync(id,
                    [new ReferenceRegisterFieldDefinition("amount", "Amount", 1, ColumnType.Decimal, false)],
                    DateTime.UtcNow, cancellation);
            }
        });

        var endpoint = services.GetRequiredService<IReferenceRegisterAdminEndpoint>();
        var details = (await endpoint.GetDetailsByCodeAsync(code))!;
        Assert.Equal(first, details.Register.RegisterId);
        Assert.Equal(mode, details.Register.RecordMode);
        Assert.Equal("amount", Assert.Single(details.Fields).ColumnCode);
        Assert.Empty(details.DimensionRules);
        Assert.Contains(await endpoint.GetListAsync(), item => item.Register.RegisterId == first && item.FieldsCount == 1);
        Assert.False((await endpoint.GetPhysicalSchemaHealthByIdAsync(first))!.Records.Exists);
        Assert.False((await endpoint.GetPhysicalSchemaHealthByIdAsync(second))!.IsOk);
        var absent = Guid.CreateVersion7();
        Assert.Null(await endpoint.GetDetailsByIdAsync(absent));
        Assert.Null(await endpoint.GetPhysicalSchemaHealthByIdAsync(absent));
        Assert.Null(await endpoint.EnsurePhysicalSchemaByIdAsync(absent));

        var maintenance = services.GetRequiredService<IReferenceRegisterAdminMaintenanceService>();
        await maintenance.EnsurePhysicalSchemasByIdsAsync([first, first, Guid.Empty, absent]);
        var healthy = (await endpoint.GetPhysicalSchemaHealthByIdAsync(first))!;
        Assert.True(healthy.IsOk);
        Assert.Equal(ReferenceRegisterNaming.RecordsTable(code), healthy.Records.TableName);
        Assert.Empty(healthy.Records.MissingColumns);
        Assert.Empty(healthy.Records.MissingIndexes);
        Assert.True(healthy.Records.HasAppendOnlyGuard);
        Assert.False((await endpoint.GetPhysicalSchemaHealthByIdAsync(second))!.Records.Exists);
        Assert.True((await endpoint.EnsurePhysicalSchemaByIdAsync(first))!.IsOk);

        var all = await endpoint.EnsurePhysicalSchemaForAllAsync();
        Assert.Equal(all.TotalCount, all.OkCount);
        Assert.All(all.Items, item => Assert.True(item.IsOk));
        var repeated = await endpoint.GetPhysicalSchemaHealthReportAsync();
        Assert.Equal(all.TotalCount, repeated.TotalCount);
        Assert.Equal(all.OkCount, repeated.OkCount);
        Assert.False((await endpoint.GetDetailsByIdAsync(first))!.Register.HasRecords);
        Assert.False((await endpoint.GetDetailsByIdAsync(second))!.Register.HasRecords);
        await Assert.ThrowsAsync<NgbArgumentRequiredException>(() => endpoint.GetDetailsByCodeAsync(" "));
    }
}
