using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.PostgreSql;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;
using CertificationApp.Definitions;
using CertificationApp.PostgreSql;
using CertificationApp.Runtime;

var builder = WebApplication.CreateBuilder(args);
var bootstrap = builder.AddNgbBackgroundJobs(PostgresHangfireJobStorageFactory.Create);
builder.Services.AddNgbRuntime()
    .AddNgbRuntimeStartupValidation()
    .AddNgbPostgres(bootstrap.ApplicationConnectionString)
    .AddNgbPostgresBackgroundJobsAdapter();
builder.Services.AddNgbPostgresExceptionMapping();
builder.Services.AddHealthChecks().AddNgbPostgresHealthCheck(bootstrap.ApplicationConnectionString);

builder.Services.AddSingleton<IDefinitionsContributor, CheckpointDefinitions>();
builder.Services.AddSingleton<IDefinitionsContributor, CheckpointRuntimeDefinitions>();
builder.Services.AddScoped<CheckpointValidator>();
builder.Services.AddScoped<ICatalogUpsertValidator>(provider =>
    provider.GetRequiredService<CheckpointValidator>());
builder.Services.AddScoped<CheckpointService>();
builder.Services.AddCheckpointPostgres();

var app = builder.Build();
app.UseNgbBackgroundJobs();
app.MapNgbBackgroundJobs();
app.Run();
