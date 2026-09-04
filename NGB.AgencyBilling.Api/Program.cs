using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using NGB.AgencyBilling.Api.Services;
using NGB.AgencyBilling.DependencyInjection;
using NGB.AgencyBilling.PostgreSql.DependencyInjection;
using NGB.AgencyBilling.Runtime.DependencyInjection;
using NGB.Api;
using NGB.Hosting.AspNetCore;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Api.Reporting;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Api.WorkCenter;
using NGB.Application.Abstractions.Services;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;
using NGB.Tools.Exceptions;

const string projectName = "NGB: Agency Billing - API";

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddSerilog();

var cs = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(cs))
    throw new NgbConfigurationViolationException("Please provide PostgreSQL connection string in 'ConnectionStrings:DefaultConnection'.");

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
    .AddAgencyBillingModule()
    .AddAgencyBillingRuntimeModule()
    .AddAgencyBillingPostgresModule();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddControllersApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddExternalLinks(builder.Configuration);
builder.Services.AddGlobalErrorHandling();
builder.Services.AddNgbWorkCenterRealtime();
builder.Services.AddNgbWorkCenterOutboxProcessing(builder.Configuration);

builder.Services.AddScoped<IMainMenuContributor, AgencyBillingMainMenuContributor>();
builder.Services.AddScoped<AgencyBillingCommandPaletteSearchService>();

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

public partial class Program;
