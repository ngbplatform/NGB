using System.Net;
using System.Net.Http.Headers;
using EquipmentInspection.Api;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NGB.Definitions;
using NGB.Definitions.Documents.Actions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Security;
using NGB.Runtime.UnitOfWork;
using Testcontainers.PostgreSql;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

internal sealed class InspectionHost(
    bool registerWorkflowHandler = true,
    string? browserOrigin = null,
    Action<IServiceCollection>? configureServices = null,
    bool notesEnabled = false,
    IReadOnlyDictionary<string, string>? configuration = null,
    int port = 0,
    Func<string, Task>? prepareDatabase = null) : IAsyncDisposable
{
    public const string AdministratorRoleCode = ApplicationRoles.Administrator;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TestIdentity _identity = new(browserOrigin);
    private WebApplicationFactory<global::Program>? _app;
    private Uri? _origin;

    public IServiceProvider Services => _app!.Services;

    public string ConnectionString => _postgres.GetConnectionString();
    public string IdentityIssuer => _identity.Issuer;

    public async Task StartAsync()
    {
        await _postgres.StartAsync();
        var connection = _postgres.GetConnectionString();
        if (prepareDatabase is not null)
        {
            await prepareDatabase(connection);
        }
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + connection]));
        Assert.Equal(0, await Migrator.Program.Main(["--connection=" + connection]));
        await Setup.Program.Main(["ConnectionStrings:DefaultConnection=" + connection]);
        await Setup.Program.Main(["ConnectionStrings:DefaultConnection=" + connection]);

        await _identity.StartAsync();
        _app = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connection);
            builder.UseSetting("KeycloakSettings:Issuer", _identity.Issuer);
            builder.UseSetting("KeycloakSettings:RequireHttpsMetadata", "false");
            builder.UseSetting("KeycloakSettings:ClientIds:0", TestIdentity.ClientId);
            builder.UseSetting("FeatureManagement:Notes", notesEnabled.ToString());
            foreach (var (key, value) in _identity.AdministrationSettings)
                builder.UseSetting(key, value);
            if (configuration is not null)
            {
                foreach (var (key, value) in configuration)
                    builder.UseSetting(key, value);
            }
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(InspectionAccessController).Assembly);
                services.AddSingleton<INgbPermissionDefinitionSource, InspectionPermissionSource>();
                if (!registerWorkflowHandler)
                {
                    var workflow = services.Single(service =>
                        service.ImplementationType == typeof(InspectionWorkflowDefinitions));
                    services.Remove(workflow);
                    services.AddSingleton<IDocumentActionDefinitionsContributor, InspectionActions>();
                }
                configureServices?.Invoke(services);
            });
        });
        _app.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        _app.StartServer();
        var origin = _app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        _origin = new Uri(origin);
    }

    public object BrowserSettings => new
    {
        api = _origin!.ToString().TrimEnd('/'),
        issuer = _identity.Issuer,
        clientId = TestIdentity.ClientId,
        password = _identity.Password
    };

    public HttpClient CreateAnonymousClient() => new()
    {
        BaseAddress = _origin ?? throw new InvalidOperationException("Start the test host first."),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<HttpClient> CreateClientAsync(string username, bool authorizationCode = false)
    {
        var token = authorizationCode
            ? await _identity.AuthorizationCodeTokenAsync(username)
            : await _identity.TokenAsync(username);
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Initial fixture setup only: repository writes do not invalidate cached access.
    // Use separate actors for independent cases, or the public security API when
    // testing role changes after an actor has already made an authenticated request.
    public async Task LinkUserAsync(string username, bool isActive, string roleCode, string roleName)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var roles = scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>();
        var assignments = scope.ServiceProvider.GetRequiredService<IPlatformUserRoleRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await unitOfWork.ExecuteInUowTransactionAsync(async ct =>
        {
            var user = await users.UpsertAsync(_identity.Subject(username), username + "@example.test",
                username, isActive, ct);
            var role = await roles.GetByCodeAsync(roleCode, ct);
            if (role is null)
            {
                role = await roles.UpsertAsync(Guid.CreateVersion7(), roleCode, roleName,
                    description: null, isSystem: false, isActive: true, ct);
            }
            await assignments.ReplaceUserRolesAsync(user, [role.RoleId], assignedByUserId: null, ct);
        });
    }

    public async Task DeactivateUserAsync(string username)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.ExecuteInUowTransactionAsync(async ct =>
        {
            var user = await users.GetByAuthSubjectAsync(_identity.Subject(username), ct)
                ?? throw new InvalidOperationException("Link the test user before deactivation.");
            await users.SetActiveAsync(user.UserId, false, ct);
        });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_app is not null)
                await _app.DisposeAsync();
        }
        finally
        {
            try
            {
                await _identity.DisposeAsync();
            }
            finally
            {
                await _postgres.DisposeAsync();
            }
        }
    }
}
