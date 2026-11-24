using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EquipmentInspection.IntegrationTests;

internal sealed class TestInfrastructure : IAsyncDisposable
{
    private static readonly object Gate = new();
    private static Task<TestInfrastructure>? _instance;

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly PostgreSqlContainer _postgres;
    private readonly string _password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly IContainer _keycloak;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    private TestInfrastructure()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithNetwork(_network)
            .WithNetworkAliases("database")
            .Build();
        _keycloak = IdentityBuilder().Build();
    }

    private ContainerBuilder IdentityBuilder()
    {
        return new ContainerBuilder("quay.io/keycloak/keycloak:26.5.6")
            .WithPortBinding(8080, true)
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "fixture-admin")
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", _password)
            .WithCommand("start-dev", "--hostname-strict=false")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(8080).ForPath("/realms/master/.well-known/openid-configuration")));
    }

    public IContainer CreateIdentityServer(TestDatabase store)
    {
        var connection = new NpgsqlConnectionStringBuilder(store.ConnectionString);
        return IdentityBuilder()
            .WithNetwork(_network)
            .WithEnvironment("KC_DB", "postgres")
            .WithEnvironment("KC_DB_URL", "jdbc:postgresql://database:5432/" + connection.Database)
            .WithEnvironment("KC_DB_USERNAME", connection.Username!)
            .WithEnvironment("KC_DB_PASSWORD", connection.Password!)
            .Build();
    }

    public string IdentityOrigin => $"http://{_keycloak.Hostname}:{_keycloak.GetMappedPublicPort(8080)}";

    public static Task<TestInfrastructure> GetAsync()
    {
        lock (Gate)
        {
            return _instance ??= StartAsync();
        }
    }

    public static async Task ShutdownAsync()
    {
        Task<TestInfrastructure>? instance;
        lock (Gate)
        {
            instance = _instance;
            _instance = null;
        }

        if (instance is not null)
            await (await instance).DisposeAsync();
    }

    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var name = "inspection_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        // The identifier is generated here, never supplied by an application or agent.
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync();
        var settings = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name };
        return new TestDatabase(_postgres.GetConnectionString(), settings.ConnectionString, name);
    }

    public async Task<byte[]> RestoreBackupAsync(string sourceConnection, string destinationConnection)
    {
        var source = new NpgsqlConnectionStringBuilder(sourceConnection);
        var destination = new NpgsqlConnectionStringBuilder(destinationConnection);
        if (source.Database == destination.Database)
            throw new ArgumentException("Use a separate disposable database for the restore.");

        var path = "/tmp/" + Guid.NewGuid().ToString("N") + ".dump";
        try
        {
            var dump = await _postgres.ExecAsync(["pg_dump", "-U", source.Username!, "-Fc", "-f", path, source.Database!]);
            if (dump.ExitCode != 0)
                throw new InvalidOperationException(dump.Stderr);

            var restore = await _postgres.ExecAsync(["pg_restore", "--exit-on-error", "-U", destination.Username!,
                "-d", destination.Database!, path]);
            if (restore.ExitCode != 0)
                throw new InvalidOperationException(restore.Stderr);
            return await _postgres.ReadFileAsync(path);
        }
        finally
        {
            await _postgres.ExecAsync(["rm", "-f", path]);
        }
    }

    public async Task CreateRealmAsync(object realm, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, origin + "/admin/realms")
        {
            Content = JsonContent.Create(realm)
        };
        using var response = await SendAdministrationAsync(request, origin);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteRealmAsync(string realm, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, origin + "/admin/realms/" + Uri.EscapeDataString(realm));
        using var response = await SendAdministrationAsync(request, origin);
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        try
        {
            await _keycloak.DisposeAsync();
        }
        finally
        {
            try
            {
                await _postgres.DisposeAsync();
            }
            finally
            {
                await _network.DisposeAsync();
            }
        }
    }

    private static async Task<TestInfrastructure> StartAsync()
    {
        var infrastructure = new TestInfrastructure();
        try
        {
            await infrastructure._network.CreateAsync();
            await Task.WhenAll(infrastructure._postgres.StartAsync(), infrastructure._keycloak.StartAsync());
            return infrastructure;
        }
        catch
        {
            await infrastructure.DisposeAsync();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAdministrationAsync(HttpRequestMessage request, string origin)
    {
        using var credentials = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "admin-cli",
            ["grant_type"] = "password",
            ["username"] = "fixture-admin",
            ["password"] = _password
        });
        using var authentication = await _client.PostAsync(origin + "/realms/master/protocol/openid-connect/token", credentials);
        authentication.EnsureSuccessStatusCode();
        var token = await authentication.Content.ReadFromJsonAsync<JsonElement>();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
        return await _client.SendAsync(request);
    }
}

internal sealed class TestDatabase(string administrationConnection, string connectionString, string name) : IAsyncDisposable
{
    public string ConnectionString => connectionString;

    public async ValueTask DisposeAsync()
    {
        await using var pooledConnection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(pooledConnection);
        await using var connection = new NpgsqlConnection(administrationConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
