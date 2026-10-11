using Microsoft.Extensions.DependencyInjection;
using NGB.Migrator.Core;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NgbApplication.Migrator;

if (args is not ["seed-administrator"])
    return await PlatformMigratorCli.RunAsync(args);

var connectionString = Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING")
    ?? throw new InvalidOperationException("NGB_CONNECTION_STRING is required.");
var services = new ServiceCollection();
services.AddLogging();
services.AddNgbRuntime().AddNgbPostgres(connectionString);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var roles = scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>();
var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
await AdministratorBootstrap.EnsureAsync(roles, unitOfWork, CancellationToken.None);
Console.WriteLine("Application Administrator role is available.");
return 0;
