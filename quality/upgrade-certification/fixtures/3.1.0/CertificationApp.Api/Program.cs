using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Api;
using NGB.Api.Attachments;
using NGB.Api.WorkCenter;
using NGB.Application.Abstractions.Services;
using NGB.Attachments.MinIO;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Hosting.AspNetCore;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Hosting.AspNetCore.Identity;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;
using NGB.Runtime.Security;
using CertificationApp.Api;
using CertificationApp.Definitions;
using CertificationApp.PostgreSql;
using CertificationApp.Runtime;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Host.AddSerilog();
builder.Services.AddInfrastructure(builder.Configuration, "CertificationApp");
builder.Services.AddNgbRuntime()
    .AddNgbRuntimeStartupValidation()
    .AddNgbRuntimeAuthorization()
    .AddNgbPostgres(connectionString);
builder.Services.Configure<NgbAdministratorOptions>(options =>
    options.ApplicationRoleCodes.Add("certification.administrator"));
builder.Services.AddNgbPostgresExceptionMapping();
builder.Services.AddGlobalErrorHandling();
builder.Services.AddHealthChecks().AddNgbPostgresHealthCheck(connectionString).AddKeycloak();
builder.Services.AddControllersApi();
builder.Services.AddNgbWorkCenterRealtime();
builder.Services.AddNgbWorkCenterOutboxProcessing(builder.Configuration);
builder.Services.AddNgbAttachmentsNotesApi(builder.Configuration, services =>
    services.AddNgbMinioAttachments(builder.Configuration.GetSection("Attachments:MinIO").Bind));
builder.Services.RemoveAll<IMainMenuContributor>();
builder.Services.AddScoped<IMainMenuContributor, ApplicationMenu>();
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddSingleton<IDefinitionsContributor, CheckpointDefinitions>();
builder.Services.AddSingleton<IDefinitionsContributor, CheckpointRuntimeDefinitions>();
builder.Services.AddScoped<CheckpointValidator>();
builder.Services.AddScoped<ICatalogUpsertValidator>(provider =>
    provider.GetRequiredService<CheckpointValidator>());
builder.Services.AddScoped<CheckpointService>();
builder.Services.AddCheckpointPostgres();

var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapNgbWorkCenterHub();
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();
