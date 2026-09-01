using NGB.AgencyBilling.DependencyInjection;
using NGB.AgencyBilling.PostgreSql.DependencyInjection;
using NGB.AgencyBilling.Runtime.DependencyInjection;
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create, options =>
{
    options.DashboardTitle = "NGB: Agency Billing - Background Jobs";
});

await bootstrap.EnsureInfrastructureAsync(new PostgresDatabaseProvisioner());

builder.Services
    .AddNgbRuntime()
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
