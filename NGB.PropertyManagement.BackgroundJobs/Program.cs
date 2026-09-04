using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.BackgroundJobs.DependencyInjection;
using NGB.PropertyManagement.DependencyInjection;
using NGB.PropertyManagement.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.Runtime.DependencyInjection;
using NGB.PropertyManagement.Security;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new PropertyManagementDemoAdministratorOptions(
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_ID"],
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_EMAIL"],
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_FIRST_NAME"],
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_LAST_NAME"]));

var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create, options =>
{
    options.DashboardTitle = "NGB: Property Management - Background Jobs";
});

builder.Services.AddNgbPostgresExceptionMapping();
builder.Services.AddHealthChecks()
    .AddNgbPostgresHealthCheck(bootstrap.ApplicationConnectionString, bootstrap.Options.PostgresHealthCheckName);

await bootstrap.EnsureInfrastructureAsync(new PostgresDatabaseProvisioner());

builder.Services
    .AddNgbRuntime()
    .AddNgbRuntimeStartupValidation()
    .AddNgbPostgres(bootstrap.ApplicationConnectionString)
    .AddNgbPostgresBackgroundJobsAdapter()
    .AddPropertyManagementModule()
    .AddPropertyManagementRuntimeModule()
    .AddPropertyManagementPostgresModule()
    .AddPropertyManagementBackgroundJobsModule();

var app = builder.Build();

app.UseNgbBackgroundJobs();
app.MapNgbBackgroundJobs();

app.Run();

public partial class Program; // workaround for Integration Tests: class must be `public`
