using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using NGB.Api;
using NGB.Hosting.AspNetCore;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Api.Reporting;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Api.WorkCenter;
using NGB.Application.Abstractions.Services;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.Api.Services;
using NGB.PropertyManagement.DependencyInjection;
using NGB.PropertyManagement.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.Runtime.DependencyInjection;
using NGB.PropertyManagement.Security;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;
using NGB.Tools.Exceptions;

const string projectName = "NGB: Property Management - API";

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddSerilog();

var cs = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(cs))
    throw new NgbConfigurationViolationException("Please provide PostgreSQL connection string in 'ConnectionStrings:DefaultConnection'.");

builder.Services.AddSingleton(new PropertyManagementDemoAdministratorOptions(
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_ID"],
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_EMAIL"],
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_FIRST_NAME"],
    builder.Configuration["KEYCLOAK_DEMO_ADMIN_LAST_NAME"]));

builder.Services.AddNgbPostgresExceptionMapping();
builder.Services.AddHealthChecks()
    .AddWebApplication()
    .AddNgbPostgresHealthCheck(cs)
    .AddKeycloak()
    .AddNgbWorkCenterHealth();

builder.Services.AddInfrastructure(builder.Configuration, projectName);

builder.Services
    .AddNgbRuntime()
    .AddNgbRuntimeStartupValidation()
    .AddNgbRuntimeAuthorization()
    .AddNgbPostgres(cs)
    .AddPropertyManagementModule()
    .AddPropertyManagementRuntimeModule()
    .AddPropertyManagementPostgresModule();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddControllersApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddExternalLinks(builder.Configuration);
builder.Services.AddGlobalErrorHandling();
builder.Services.AddNgbWorkCenterRealtime();
builder.Services.AddNgbWorkCenterOutboxProcessing(builder.Configuration);

builder.Services.AddScoped<IMainMenuContributor, PropertyManagementMainMenuContributor>();
builder.Services.AddScoped<ICommandPaletteSearchService, CommandPaletteSearchService>();

builder.Services.RemoveAll<IReportVariantAccessContext>();
builder.Services.AddScoped<IReportVariantAccessContext, HttpReportVariantAccessContext>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(projectName);
}

app
    .UseSerilogRequestLogging()
    .UseHttpsRedirection()
    .UseCompletelyAllowedCorsPolicy()
    .UseHealthChecks();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapNgbWorkCenterHub();

app.Run();

public partial class Program; // workaround for Integration Tests: class must be `public`
