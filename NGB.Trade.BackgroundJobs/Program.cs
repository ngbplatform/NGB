using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;
using NGB.Trade.DependencyInjection;
using NGB.Trade.PostgreSql.DependencyInjection;
using NGB.Trade.Runtime.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create, options =>
{
    options.DashboardTitle = "NGB: Trade - Background Jobs";
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
    .AddTradeModule()
    .AddTradeRuntimeModule()
    .AddTradePostgresModule();

var app = builder.Build();

app.UseNgbBackgroundJobs();
app.MapNgbBackgroundJobs();

app.Run();

public partial class Program;
