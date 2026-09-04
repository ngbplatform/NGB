using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.BackgroundJobs.Catalog;
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.CRM.DependencyInjection;
using NGB.CRM.BackgroundJobs;
using NGB.CRM.PostgreSql.DependencyInjection;
using NGB.CRM.Runtime.DependencyInjection;
using NGB.CRM.Security;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new CrmDemoAdministratorOptions(
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_ID"],
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_EMAIL"],
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_FIRST_NAME"],
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_LAST_NAME"]));

var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create, options =>
{
    options.DashboardTitle = "NGB: CRM - Background Jobs";
});

builder.Services.AddNgbPostgresExceptionMapping();
builder.Services.AddHealthChecks()
    .AddNgbPostgresHealthCheck(bootstrap.ApplicationConnectionString, bootstrap.Options.PostgresHealthCheckName);

builder.Services.RemoveAll<IBackgroundJobCatalogContributor>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IBackgroundJobCatalogContributor, CrmBackgroundJobCatalogContributor>());
builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CrmObsoleteRecurringJobsCleanupHostedService>());

await bootstrap.EnsureInfrastructureAsync(new PostgresDatabaseProvisioner());

builder.Services
    .AddNgbRuntime()
    .AddNgbRuntimeStartupValidation()
    .AddNgbPostgres(bootstrap.ApplicationConnectionString)
    .AddNgbPostgresBackgroundJobsAdapter()
    .AddCrmModule()
    .AddCrmRuntimeModule()
    .AddCrmPostgresModule();

var app = builder.Build();

app.UseNgbBackgroundJobs();
app.MapNgbBackgroundJobs();

app.Run();

public partial class Program;
