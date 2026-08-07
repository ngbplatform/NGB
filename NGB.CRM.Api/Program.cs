using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Api;
using NGB.Api.GlobalErrorHandling;
using NGB.Api.Reporting;
using NGB.Api.Sso;
using NGB.Api.WorkCenter;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Api.Services;
using NGB.CRM.DependencyInjection;
using NGB.CRM.PostgreSql.DependencyInjection;
using NGB.CRM.Runtime.DependencyInjection;
using NGB.Runtime.Reporting.Datasets;
using NGB.Runtime.Reporting.Definitions;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Serilog;

const string projectName = "NGB: CRM - API";

var builder = WebApplication.CreateBuilder(args);

builder.Host.AddSerilog();

builder.Services.AddHealthChecks()
    .AddWebApplication()
    .AddPostgres(builder.Configuration)
    .AddKeycloak()
    .AddNgbWorkCenterHealth();

builder.Services.AddInfrastructure(builder.Configuration, projectName);

var cs = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(cs))
    throw new NgbConfigurationViolationException("Please provide PostgreSQL connection string in 'ConnectionStrings:DefaultConnection'.");

builder.Services
    .AddNgbRuntime()
    .AddNgbRuntimeAuthorization()
    .AddNgbPostgres(cs)
    .AddCrmModule()
    .AddCrmRuntimeModule()
    .AddCrmPostgresModule();

builder.Services.RemoveAll<IMainMenuContributor>();
RemoveRegistration<INgbPermissionDefinitionSource, PlatformPermissionDefinitionSource>(builder.Services);
RemoveRegistration<IReportDefinitionSource, AccountingLedgerAnalysisDefinitionSource>(builder.Services);
RemoveRegistration<IReportDefinitionSource, CanonicalAccountingReportDefinitionSource>(builder.Services);
RemoveRegistration<IReportDatasetSource, AccountingLedgerAnalysisDatasetSource>(builder.Services);
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<INgbPermissionDefinitionSource, CrmPlatformPermissionDefinitionSource>());

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddControllersApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddExternalLinks(builder.Configuration);
builder.Services.AddGlobalErrorHandling();
builder.Services.AddNgbWorkCenterRealtime();
builder.Services.Configure<MvcOptions>(options => options.Conventions.Add(new CrmApplicationSurfaceConvention()));

builder.Services.AddScoped<IMainMenuContributor, CrmMainMenuContributor>();
builder.Services.AddScoped<CrmCommandPaletteSearchService>();
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

static void RemoveRegistration<TService, TImplementation>(IServiceCollection services)
{
    for (var i = services.Count - 1; i >= 0; i--)
    {
        var descriptor = services[i];
        if (descriptor.ServiceType == typeof(TService)
            && descriptor.ImplementationType == typeof(TImplementation))
        {
            services.RemoveAt(i);
        }
    }
}

public partial class Program;
