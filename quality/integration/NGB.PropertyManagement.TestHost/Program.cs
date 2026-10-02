using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.Testing.Minio;

// Playwright owns this process. Configuration travels through stdin and a private temporary file.
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var input = JsonSerializer.Deserialize<HostInput>((await Console.In.ReadLineAsync())!, options)!;
var fixture = new PmIntegrationFixture { BrowserOrigin = input.BrowserOrigin };
await using var storage = new MinioIntegrationFixture(input.BrowserOrigin);

try
{
    await fixture.InitializeAsync();
    await fixture.ResetDatabaseAsync();
    await storage.InitializeAsync();

    await using var factory = new PmApiFactory(fixture, new Dictionary<string, string?>
    {
        ["Attachments:MinIO:InternalEndpoint"] = storage.Endpoint,
        ["Attachments:MinIO:PublicEndpoint"] = storage.Endpoint,
        ["Attachments:MinIO:AccessKey"] = storage.AccessKey,
        ["Attachments:MinIO:SecretKey"] = storage.SecretKey,
        ["Attachments:MinIO:Bucket"] = storage.Bucket,
        ["Attachments:MinIO:AllowInsecureHttp"] = bool.TrueString
    });

    factory.UseKestrel(0);
    using var client = factory.CreateClient();
    var api = factory.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    var settings = new
    {
        api,
        storage = storage.Endpoint,
        token = client.DefaultRequestHeaders.Authorization!.Parameter,
        keycloakUrl = fixture.Keycloak.BaseUrl,
        realm = fixture.Keycloak.Realm,
        clientId = PmKeycloakTestClients.WebClient,
        username = PmKeycloakTestUsers.Admin.Username,
        password = PmKeycloakTestUsers.Admin.Password
    };

    await File.WriteAllTextAsync(input.SettingsFile, JsonSerializer.Serialize(settings, options));
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(input.SettingsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

    Console.WriteLine("NGB_CONTENT_STACK_READY");
    await Console.Out.FlushAsync();
    await Console.In.ReadLineAsync();
}
finally
{
    await fixture.DisposeAsync();
}

internal sealed record HostInput(string BrowserOrigin, string SettingsFile);
