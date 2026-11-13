using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Security;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;

namespace EquipmentInspection.IntegrationTests;

internal static class BrowserTestHost
{
    public static async Task Main()
    {
        try
        {
            await RunAsync();
        }
        finally
        {
            await TestInfrastructure.ShutdownAsync();
        }
    }

    private static async Task RunAsync()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var input = JsonSerializer.Deserialize<Input>((await Console.In.ReadLineAsync())!, options)!;
        await using var storage = new TestAttachmentStorage(input.BrowserOrigin);
        await storage.StartAsync();
        await using var host = new InspectionHost(browserOrigin: input.BrowserOrigin,
            notesEnabled: true, configuration: storage.Configuration);
        await host.StartAsync();
        await host.LinkUserAsync("administrator", true, InspectionHost.AdministratorRoleCode, "Application administrator");
        await host.LinkUserAsync("imitator", true, "example.content-reader", "Content reader");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var role = await services.GetRequiredService<IPlatformRoleRepository>().GetByCodeAsync("example.content-reader");
            await services.GetRequiredService<IUnitOfWork>().ExecuteInUowTransactionAsync(ct =>
                services.GetRequiredService<IPermissionSnapshotRepository>().ReplaceRolePermissionsAsync(role!.RoleId,
                    [
                        new NgbPermissionKey(NgbResourceKinds.Catalog, EquipmentDefinitions.EquipmentCode, NgbPermissionActions.View),
                        NgbSystemPermissions.NotesRead,
                        NgbSystemPermissions.AttachmentsRead
                    ], ct));
        }
        await File.WriteAllTextAsync(input.SettingsFile, JsonSerializer.Serialize(host.BrowserSettings, options));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(input.SettingsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Console.WriteLine("EQUIPMENT_INSPECTION_READY");
        await Console.Out.FlushAsync();
        await Console.In.ReadLineAsync();
    }

    private sealed record Input(string BrowserOrigin, string SettingsFile);
}
