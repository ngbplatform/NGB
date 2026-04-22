using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using NGB.Api;
using NGB.Api.GlobalErrorHandling;
using NGB.Api.Reporting;
using NGB.Api.Sso;
using NGB.Application.Abstractions.Services;
using NGB.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.Api.Services;
using NGB.PropertyManagement.DependencyInjection;
using NGB.PropertyManagement.PostgreSql.DependencyInjection;
using NGB.PropertyManagement.Runtime.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Tools.Exceptions;

const string projectName = "NGB: Property Management - API";

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddSerilog();

builder.Services.AddHealthChecks()
    .AddWebApplication()
    .AddPostgres(builder.Configuration)
    .AddKeycloak();

builder.Services.AddInfrastructure(builder.Configuration, projectName);

var cs = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(cs))
    throw new NgbConfigurationViolationException("Please provide PostgreSQL connection string in 'ConnectionStrings:DefaultConnection'.");

builder.Services
    .AddNgbRuntime()
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

app.Run();

public partial class Program; // workaround for Integration Tests: class must be `public`
