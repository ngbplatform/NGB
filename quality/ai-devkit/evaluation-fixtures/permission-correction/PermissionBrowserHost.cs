using System.Text.Json;
using NGB.Contracts.Services;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

internal static class PermissionBrowserHost
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
        await using var host = new InspectionHost(browserOrigin: input.BrowserOrigin);
        await host.StartAsync();
        await host.LinkUserAsync("application-administrator", true, PermissionAcceptance.AdministratorCode, "Renamed supervisors");
        await host.LinkUserAsync("inactive-administrator", false, "equipment.inactive-supervisors", "Inactive supervisors");
        await host.LinkUserAsync("imitator", true, "equipment.observer", "Administrator");
        await PermissionAcceptance.AddReaderAsync(host);
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, PermissionAcceptance.EquipmentPath, Payload(("display", "Browser protected equipment")));
        await File.WriteAllTextAsync(input.SettingsFile, JsonSerializer.Serialize(new
        {
            identity = host.BrowserSettings,
            equipmentId = equipment.Id
        }, options));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(input.SettingsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Console.WriteLine("PERMISSION_ORACLE_READY");
        await Console.Out.FlushAsync();
        await Console.In.ReadLineAsync();
    }

    private sealed record Input(string BrowserOrigin, string SettingsFile);
}
