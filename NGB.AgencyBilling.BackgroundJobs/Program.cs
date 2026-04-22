using NGB.AgencyBilling.DependencyInjection;
using NGB.AgencyBilling.PostgreSql.DependencyInjection;
using NGB.AgencyBilling.Runtime.DependencyInjection;
using NGB.BackgroundJobs.Hosting;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var bootstrap = builder.AddNgbBackgroundJobs(options =>
{
    options.DashboardTitle = "NGB: Agency Billing - Background Jobs";
});

await bootstrap.EnsureInfrastructureAsync();

builder.Services
    .AddNgbRuntime()
    .AddNgbPostgres(bootstrap.ApplicationConnectionString)
    .AddAgencyBillingModule()
    .AddAgencyBillingRuntimeModule()
    .AddAgencyBillingPostgresModule();

var app = builder.Build();

app.UseNgbBackgroundJobs();
app.MapNgbBackgroundJobs();

app.Run();

public partial class Program;
