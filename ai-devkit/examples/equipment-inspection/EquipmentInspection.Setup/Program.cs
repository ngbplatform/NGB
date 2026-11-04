using EquipmentInspection.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;

namespace EquipmentInspection.Setup;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        builder.Services.AddNgbRuntime().AddNgbPostgres(connectionString);
        builder.Services.AddScoped<EquipmentDefaults>();
        builder.Services.AddScoped<EquipmentConditionDefaults>();
        builder.Services.AddScoped<EquipmentServiceDefaults>();
        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EquipmentDefaults>().ApplyAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<EquipmentConditionDefaults>().ApplyAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<EquipmentServiceDefaults>().ApplyAsync(CancellationToken.None);
    }
}
