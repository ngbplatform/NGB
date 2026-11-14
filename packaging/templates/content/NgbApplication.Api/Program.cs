using NgbApplication;
using Microsoft.AspNetCore.Authorization;
using NGB.AiAssistant.AspNetCore;
using NGB.AiAssistant.Ollama;
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
using NGB.Runtime.Admin;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Hosting;
using NGB.Runtime.Security;
using NgbApplication.Api;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
}

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
// This minimal starter has no Accounting controllers or UI routes. When adding
// Accounting, retain these contributors and compose their destinations together.
// Preserve every other contributor registered by application or platform modules.
var accountingMenuTypes = new[]
{
    typeof(AccountingDocumentsMainMenuContributor),
    typeof(AccountingReportsMainMenuContributor),
    typeof(AccountingAdminMainMenuContributor)
};
foreach (var descriptor in builder.Services.Where(descriptor =>
    descriptor.ServiceType == typeof(IMainMenuContributor)
    && descriptor.ImplementationType is { } type
    && accountingMenuTypes.Contains(type)).ToArray())
{
    builder.Services.Remove(descriptor);
}
builder.Services.AddScoped<IMainMenuContributor, ApplicationMenu>();
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddNgbMcp(builder.Configuration);
builder.Services.AddNgbAiAssistant(builder.Configuration, services =>
    services.AddNgbOllamaChatClient(builder.Configuration.GetSection(OllamaAssistantOptions.SectionName).Bind));

var app = builder.Build();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapNgbMcp();
app.MapNgbAiAssistant();
app.MapNgbWorkCenterHub();
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();

public partial class Program
{
}
