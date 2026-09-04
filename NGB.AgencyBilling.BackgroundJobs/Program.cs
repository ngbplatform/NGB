using NGB.AgencyBilling.DependencyInjection;
using NGB.AgencyBilling.PostgreSql.DependencyInjection;
using NGB.AgencyBilling.Runtime.DependencyInjection;
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;

var builder = WebApplication.CreateBuilder(args);

var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create, options =>
{
    options.DashboardTitle = "NGB: Agency Billing - Background Jobs";
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
    .AddAgencyBillingModule()
    .AddAgencyBillingRuntimeModule()
    .AddAgencyBillingPostgresModule();

var app = builder.Build();

app.UseNgbBackgroundJobs();
app.MapNgbBackgroundJobs();

app.Run();

public partial class Program;
