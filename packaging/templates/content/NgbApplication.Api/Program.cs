using NgbApplication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Api;
using NGB.Api.Attachments;
using NGB.Api.WorkCenter;
using NGB.Application.Abstractions.Services;
using NGB.Hosting.AspNetCore;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Hosting.AspNetCore.Identity;
using NGB.Mcp.AspNetCore;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;
using NGB.Runtime.Security;
using NgbApplication.Api;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Host.AddSerilog();
builder.Services.AddInfrastructure(builder.Configuration, "NgbApplication");
builder.Services.AddNgbRuntime()
    .AddNgbRuntimeStartupValidation()
    .AddNgbRuntimeAuthorization()
    .AddNgbPostgres(connectionString);
builder.Services.Configure<NgbAdministratorOptions>(options =>
    options.ApplicationRoleCodes.Add(ApplicationRoles.Administrator));
builder.Services.AddNgbPostgresExceptionMapping();
builder.Services.AddGlobalErrorHandling();
builder.Services.AddHealthChecks().AddNgbPostgresHealthCheck(connectionString).AddKeycloak();
builder.Services.AddControllersApi();
builder.Services.AddNgbWorkCenterRealtime();
builder.Services.AddNgbWorkCenterOutboxProcessing(builder.Configuration);
builder.Services.AddNgbAttachmentsNotesApi(builder.Configuration);
builder.Services.RemoveAll<IMainMenuContributor>();
builder.Services.AddScoped<IMainMenuContributor, ApplicationMenu>();
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddNgbMcp(builder.Configuration);

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapNgbMcp();
app.MapNgbWorkCenterHub();
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();
