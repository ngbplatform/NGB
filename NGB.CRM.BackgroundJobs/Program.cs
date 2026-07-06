using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Hosting;
using NGB.CRM.DependencyInjection;
using NGB.CRM.BackgroundJobs;
using NGB.CRM.PostgreSql.DependencyInjection;
using NGB.CRM.Runtime.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var bootstrap = builder.AddNgbBackgroundJobs(options =>
{
    options.DashboardTitle = "NGB: CRM - Background Jobs";
});

builder.Services.RemoveAll<IBackgroundJobCatalogContributor>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IBackgroundJobCatalogContributor, CrmBackgroundJobCatalogContributor>());
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CrmObsoleteRecurringJobsCleanupHostedService>());

await bootstrap.EnsureInfrastructureAsync();

builder.Services
    .AddNgbRuntime()
    .AddNgbPostgres(bootstrap.ApplicationConnectionString)
    .AddCrmModule()
    .AddCrmRuntimeModule()
    .AddCrmPostgresModule();

var app = builder.Build();

app.UseNgbBackgroundJobs();
app.MapNgbBackgroundJobs();

app.Run();

public partial class Program;
