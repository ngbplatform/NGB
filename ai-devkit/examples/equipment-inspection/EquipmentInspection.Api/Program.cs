using EquipmentInspection.Api;
using EquipmentInspection.PostgreSql;
using EquipmentInspection.Runtime;
using Microsoft.AspNetCore.Authorization;
using NGB.AiAssistant.AspNetCore;
using NGB.Api;
using NGB.Api.Attachments;
using NGB.Api.Reporting;
using NGB.Api.WorkCenter;
using NGB.Application.Abstractions.Services;
using NGB.Attachments.MinIO;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.Mcp;
using NGB.Mcp.AspNetCore;
using NGB.PostgreSql.AspNetCore.DependencyInjection;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;
using NGB.Runtime.Security;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Services.AddInfrastructure(builder.Configuration, "Equipment inspection");
builder.Services.AddNgbRuntime()
    .AddNgbRuntimeStartupValidation()
    .AddNgbRuntimeAuthorization()
    .AddNgbPostgres(connectionString);
builder.Services.AddNgbPostgresExceptionMapping();
builder.Services.AddGlobalErrorHandling();
var attachmentConfiguration = builder.Configuration.GetSection("Attachments:MinIO");
builder.Services.AddNgbAttachmentsNotesApi(builder.Configuration, services =>
    services.AddNgbMinioAttachments(attachmentConfiguration.Bind));
builder.Services.AddControllersApi();
builder.Services.AddScoped<IReportVariantAccessContext, HttpReportVariantAccessContext>();
builder.Services.AddNgbWorkCenterRealtime();
builder.Services.AddNgbWorkCenterOutboxProcessing(builder.Configuration);
builder.Services.AddHealthChecks().AddNgbPostgresHealthCheck(connectionString);
builder.Services.AddEquipmentRuntime();
builder.Services.AddEquipmentPersistence();
builder.Services.AddScoped<EquipmentReadTool>();
builder.Services.AddNgbMcpContributor<EquipmentReadToolContributor>();
builder.Services.AddNgbMcp(builder.Configuration);
builder.Services.AddEquipmentAssistant(builder.Configuration);
builder.Services.Configure<NgbAdministratorOptions>(options =>
    options.ApplicationRoleCodes.Add(ApplicationRoles.Administrator));
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapNgbWorkCenterHub();
app.MapNgbMcp();
app.MapNgbAiAssistant();
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();

public partial class Program
{
}
