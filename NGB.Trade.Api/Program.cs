using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using NGB.Api;
using NGB.Api.GlobalErrorHandling;
using NGB.Api.Reporting;
using NGB.Api.Sso;
using NGB.Application.Abstractions.Services;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Tools.Exceptions;
using NGB.Trade.Api.Services;
using NGB.Trade.DependencyInjection;
using NGB.Trade.PostgreSql.DependencyInjection;
using NGB.Trade.Runtime.DependencyInjection;

const string projectName = "NGB: Trade - API";

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
    .AddTradeModule()
    .AddTradeRuntimeModule()
    .AddTradePostgresModule();

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddControllersApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddExternalLinks(builder.Configuration);
builder.Services.AddGlobalErrorHandling();

builder.Services.AddScoped<IMainMenuContributor, TradeMainMenuContributor>();
builder.Services.AddScoped<TradeCommandPaletteSearchService>();

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

public partial class Program;
