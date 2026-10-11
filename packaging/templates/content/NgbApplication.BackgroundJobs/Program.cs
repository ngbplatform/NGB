using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;

var builder = WebApplication.CreateBuilder(args);
var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create);
builder.Services.AddNgbRuntime()
    .AddNgbRuntimeStartupValidation()
    .AddNgbPostgres(bootstrap.ApplicationConnectionString)
    .AddNgbPostgresBackgroundJobsAdapter();
builder.Services.AddNgbPostgresExceptionMapping();
builder.Services.AddHealthChecks().AddNgbPostgresHealthCheck(bootstrap.ApplicationConnectionString);

var app = builder.Build();
app.UseNgbBackgroundJobs();
app.MapNgbBackgroundJobs();
app.Run();
