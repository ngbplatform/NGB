using NGB.BackgroundJobs.Hosting;
using NGB.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.BackgroundJobs.DependencyInjection;
using NGB.PropertyManagement.DependencyInjection;
using NGB.PropertyManagement.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.Runtime.DependencyInjection;
using NGB.Runtime.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var bootstrap = builder.AddNgbBackgroundJobs(options =>
{
    options.DashboardTitle = "NGB: Property Management - Background Jobs";
});

await bootstrap.EnsureInfrastructureAsync();

builder.Services
    .AddNgbRuntime()
    .AddNgbPostgres(bootstrap.ApplicationConnectionString)
    .AddPropertyManagementModule()
    .AddPropertyManagementRuntimeModule()
    .AddPropertyManagementPostgresModule()
    .AddPropertyManagementBackgroundJobsModule();

var app = builder.Build();

app.UseNgbBackgroundJobs();
app.MapNgbBackgroundJobs();

app.Run();

public partial class Program; // workaround for Integration Tests: class must be `public`
