using EquipmentInspection.PostgreSql;
using EquipmentInspection.Runtime;
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;

namespace EquipmentInspection.BackgroundJobs;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create,
            options => options.WorkerCount = 2);
        builder.Services.AddNgbRuntime()
            .AddNgbRuntimeStartupValidation()
            .AddNgbPostgres(bootstrap.ApplicationConnectionString)
            .AddNgbPostgresBackgroundJobsAdapter();
        builder.Services.AddNgbPostgresExceptionMapping();
        builder.Services.AddHealthChecks().AddNgbPostgresHealthCheck(bootstrap.ApplicationConnectionString);
        builder.Services.AddEquipmentRuntime();
        builder.Services.AddEquipmentPersistence();

        var app = builder.Build();
        app.UseNgbBackgroundJobs();
        app.MapNgbBackgroundJobs();
        await app.RunAsync();
    }
}
