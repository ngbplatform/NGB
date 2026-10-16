using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using NGB.Api;
using NGB.Api.Attachments;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Core.Security;
using NGB.Mcp;
using NGB.Mcp.AspNetCore;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.Runtime.DependencyInjection;
using NGB.Testing.Mcp;
using Testcontainers.PostgreSql;
using Xunit;

namespace ExternalMcp.Tests;

public sealed class PackageConsumerTests
{
    private static readonly JsonElement PerformanceBudgets = JsonSerializer.Deserialize<JsonElement>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "performance-budgets.json")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicPackagesSupportRealUserAndScopedCustomContributor(bool customModule)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        await DatabaseBootstrapper.InitializeAsync(postgres.GetConnectionString(), default);
        if (customModule)
            await PostgresEvolveMigrator.MigrateAsync(postgres.GetConnectionString(), [typeof(CheckpointDefinitions).Assembly],
                metadataTableName: "migration_changelog__mcp_fixture");
        await using var identity = new KeycloakFixture();
        await identity.StartAsync();
        var (token, subject) = await identity.AuthenticateUserAsync();

        // Reserve a loopback endpoint for this test's canonical resource URI.
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(origin);
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["KeycloakSettings:Issuer"] = identity.Issuer,
            ["KeycloakSettings:RequireHttpsMetadata"] = "false",
            ["KeycloakSettings:ClientIds:0"] = "ngb-rest-tests",
            ["Mcp:Enabled"] = "true",
            ["Mcp:Resource"] = origin + "/mcp",
            ["Mcp:Audience"] = KeycloakFixture.Audience,
            ["Mcp:RequestsPerMinute"] = "10000",
            ["Mcp:AllowLocalhostHttp"] = "true"
        });
        builder.Services.AddInfrastructure(builder.Configuration, "Independent MCP consumer");
        builder.Services.AddNgbRuntime().AddNgbRuntimeAuthorization().AddNgbPostgres(postgres.GetConnectionString());
        builder.Services.AddNgbAttachmentsNotesApi(builder.Configuration);
        builder.Services.AddNgbMcpContributor<CurrentUserContributor>();
        builder.Services.AddScoped<CurrentUserRead>();
        if (customModule)
            builder.Services.AddCheckpoint();
        builder.Services.AddNgbMcp(builder.Configuration);
        await using var app = builder.Build();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapNgbMcp();
        app.MapGet("/api/current-user", () => "REST audience accepted").RequireAuthorization();
        await app.StartAsync();

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var rest = await http.GetAsync(origin + "/api/current-user");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, rest.StatusCode);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(origin + "/mcp") }, http));
        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "custom_current_user");
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("register", StringComparison.Ordinal));
        var response = await client.CallToolAsync("custom_current_user", new Dictionary<string, object?>());
        Assert.NotEqual(true, response.IsError);
        var data = response.StructuredContent!.Value.GetProperty("data");
        Assert.Equal(subject, data.GetProperty("subject").GetString());
        Assert.True(data.GetProperty("active").GetBoolean());

        Assert.DoesNotContain(typeof(McpReadDispatcher).Assembly.GetManifestResourceNames(),
            name => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || name.StartsWith("NGB.Mcp.Knowledge.", StringComparison.Ordinal));
        foreach (var name in new[] { "ngb_search_platform_knowledge", "ngb_get_platform_knowledge" })
        {
            Assert.DoesNotContain(tools, tool => tool.Name == name);
            var removed = await Assert.ThrowsAsync<McpProtocolException>(() => client.CallToolAsync(name,
                new Dictionary<string, object?> { ["id"] = "security-and-permissions" }).AsTask());
            Assert.Equal(McpErrorCode.InvalidParams, removed.ErrorCode);
            Assert.EndsWith("The requested read tool is unavailable.", removed.Message);
        }
        var before = await DatabaseSnapshot.CaptureAsync(postgres.GetConnectionString());
        var metadata = await CallAsync(client, "ngb_list_metadata", new());
        Assert.Equal(customModule, metadata.GetProperty("items").EnumerateArray()
            .Any(item => item.GetProperty("typeCode").GetString() == CheckpointDefinitions.Code));
        await CallAsync(client, "ngb_get_capabilities", new());
        await CallAsync(client, "ngb_list_reports", new());
        await CallAsync(client, "ngb_search", new() { ["query"] = "Checkpoint" });
        var bootstrapWork = await client.CallToolAsync("ngb_get_work_items", new Dictionary<string, object?> { ["summaryOnly"] = true });
        Assert.True(bootstrapWork.IsError);
        Assert.Equal(before, await DatabaseSnapshot.CaptureAsync(postgres.GetConnectionString()));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>()
                .UpsertAsync(subject, "alex@example.test", "Alex Carter", true);
            await uow.CommitAsync();
        }
        before = await DatabaseSnapshot.CaptureAsync(postgres.GetConnectionString());
        await CallAsync(client, "ngb_get_work_items", new() { ["summaryOnly"] = true });
        await CallAsync(client, "ngb_get_work_items", new());
        Assert.Equal(before, await DatabaseSnapshot.CaptureAsync(postgres.GetConnectionString()));
        if (customModule)
            await VerifyCustomModuleAsync(app.Services, client, identity, origin, postgres.GetConnectionString());
        await app.StopAsync();
    }

    private static async Task VerifyCustomModuleAsync(IServiceProvider services, McpClient admin, KeycloakFixture identity,
        string origin, string connectionString)
    {
        Guid recordId;
        await using (var scope = services.CreateAsyncScope())
        {
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var record = await catalogs.CreateAsync(CheckpointDefinitions.Code,
                new RecordPayload(new Dictionary<string, JsonElement> { ["display"] = JsonSerializer.SerializeToElement("Checkpoint Alpha") }), default);
            recordId = record.Id;
        }
        var request = new Dictionary<string, object?> { ["kind"] = "catalog", ["typeCode"] = CheckpointDefinitions.Code, ["id"] = recordId };
        var before = await DatabaseSnapshot.CaptureAsync(connectionString);
        var detail = await CallAsync(admin, "ngb_get_record", request);
        Assert.Equal("Checkpoint Alpha", detail.GetProperty("catalog").GetProperty("display").GetString());
        var page = await CallAsync(admin, "ngb_list_records", new() { ["kind"] = "catalog", ["typeCode"] = CheckpointDefinitions.Code, ["limit"] = 1 });
        Assert.Equal(recordId, page.GetProperty("catalogs").GetProperty("items")[0].GetProperty("id").GetGuid());
        var found = await CallAsync(admin, "ngb_search", new() { ["query"] = "Checkpoint Alpha" });
        Assert.Contains(found.GetProperty("items").EnumerateArray(), item => item.GetProperty("recordId").ValueKind == JsonValueKind.String
            && item.GetProperty("recordId").GetGuid() == recordId);
        var filteredSearch = new Dictionary<string, object?>
        {
            ["query"] = "Checkpoint Alpha",
            ["kind"] = "catalog",
            ["typeCode"] = CheckpointDefinitions.Code
        };
        var filtered = await CallAsync(admin, "ngb_search", filteredSearch);
        Assert.Equal(recordId, Assert.Single(filtered.GetProperty("items").EnumerateArray()).GetProperty("recordId").GetGuid());
        var fetched = await admin.CallToolAsync("fetch", new Dictionary<string, object?> { ["id"] = $"catalog:{CheckpointDefinitions.Code}:{recordId:D}" });
        Assert.NotEqual(true, fetched.IsError);
        Assert.Equal(before, await DatabaseSnapshot.CaptureAsync(connectionString));

        await VerifyPaginationAndQueryBudgetAsync(services, admin, connectionString);

        var (token, subject) = await identity.AuthenticateUserAsync("reader");
        Guid userId;
        var roleId = Guid.NewGuid();
        await using (var scope = services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            userId = await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>()
                .UpsertAsync(subject, "reader@example.test", "Limited Reader", true);
            await scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>()
                .UpsertAsync(roleId, "mcp-reader", "Reader", null, false, true);
            await scope.ServiceProvider.GetRequiredService<IPlatformUserRoleRepository>().ReplaceUserRolesAsync(userId, [roleId], null);
            await scope.ServiceProvider.GetRequiredService<IPermissionSnapshotRepository>().ReplaceRolePermissionsAsync(roleId,
                [new(NgbResourceKinds.Catalog, CheckpointDefinitions.Code, NgbPermissionActions.View)]);
            await scope.ServiceProvider.GetRequiredService<IUserAccessVersionRepository>().IncrementAsync(userId);
            await uow.CommitAsync();
        }
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using var reader = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(origin + "/mcp")
        }, http));
        Assert.DoesNotContain(await reader.ListToolsAsync(), tool => tool.Name == "custom_current_user");
        Assert.Equal("Checkpoint Alpha", (await CallAsync(reader, "ngb_get_record", request)).GetProperty("catalog").GetProperty("display").GetString());
        var lookup = await CallAsync(reader, "ngb_search", new() { ["query"] = "Checkpoint Alpha" });
        Assert.Empty(lookup.GetProperty("items").EnumerateArray());
        Assert.Empty((await CallAsync(reader, "ngb_search", filteredSearch)).GetProperty("items").EnumerateArray());
        await using (var scope = services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionSnapshotRepository>().ReplaceRolePermissionsAsync(roleId, []);
            await scope.ServiceProvider.GetRequiredService<IUserAccessVersionRepository>().IncrementAsync(userId);
            await uow.CommitAsync();
        }
        before = await DatabaseSnapshot.CaptureAsync(connectionString);
        var forbidden = await reader.CallToolAsync("ngb_get_record", request);
        Assert.True(forbidden.IsError);
        Assert.Empty((await CallAsync(reader, "ngb_list_metadata", new())).GetProperty("items").EnumerateArray());
        Assert.Empty((await CallAsync(reader, "ngb_search", filteredSearch)).GetProperty("items").EnumerateArray());
        Assert.NotEqual(true, (await admin.CallToolAsync("ngb_get_record", request)).IsError);
        Assert.Equal(before, await DatabaseSnapshot.CaptureAsync(connectionString));
        await using (var scope = services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>().SetActiveAsync(userId, false);
            await uow.CommitAsync();
        }
        using var inactiveRequest = new HttpRequestMessage(HttpMethod.Post, origin + "/mcp")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        using var inactiveResponse = await http.SendAsync(inactiveRequest);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, inactiveResponse.StatusCode);
    }

    private static async Task<JsonElement> CallAsync(McpClient client, string name, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        Assert.True(result.IsError != true, $"{name}: {JsonSerializer.Serialize(result.Content)}");
        return result.StructuredContent!.Value.GetProperty("data");
    }

    private static async Task VerifyPaginationAndQueryBudgetAsync(IServiceProvider services, McpClient client, string connectionString)
    {
        var expected = new HashSet<Guid>();
        foreach (var recordCountValue in PerformanceBudgets.GetProperty("recordCounts").EnumerateArray())
        {
            var recordCount = recordCountValue.GetInt32();
            await using (var scope = services.CreateAsyncScope())
            {
                var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
                for (var index = expected.Count; index < recordCount; index++)
                {
                    var record = await catalogs.CreateAsync(CheckpointDefinitions.Code, new RecordPayload(new Dictionary<string, JsonElement>
                    {
                        ["display"] = JsonSerializer.SerializeToElement($"Scale checkpoint {index:D3}")
                    }), default);
                    expected.Add(record.Id);
                }
            }
            var before = await DatabaseSnapshot.CaptureAsync(connectionString);
            var counts = new List<int>();
            foreach (var limit in new[] { 1, 10, 100 })
            {
                using var probe = new McpQueryProbe($"catalog-{recordCount}-page-{limit}");
                var page = await CallAsync(client, "ngb_list_records", new()
                {
                    ["kind"] = "catalog", ["typeCode"] = CheckpointDefinitions.Code,
                    ["limit"] = limit, ["search"] = "Scale checkpoint"
                });
                Assert.Equal(limit, page.GetProperty("catalogs").GetProperty("items").GetArrayLength());
                Assert.True(probe.CommandCount > 0, "The query-count probe must observe actual provider commands.");
                counts.Add(probe.CommandCount);
            }
            Assert.True(counts[1] <= counts[0] && counts[2] <= counts[0], "Larger pages must not add database round trips.");

            var seen = new HashSet<Guid>();
            string? cursor = null;
            do
            {
                var response = await CallAsync(client, "ngb_list_records", new()
                {
                    ["kind"] = "catalog", ["typeCode"] = CheckpointDefinitions.Code,
                    ["limit"] = 100, ["search"] = "Scale checkpoint", ["cursor"] = cursor
                });
                var page = response.GetProperty("catalogs");
                foreach (var item in page.GetProperty("items").EnumerateArray())
                    Assert.True(seen.Add(item.GetProperty("id").GetGuid()), "A continuation must not repeat a record.");
                Assert.True(seen.Count <= expected.Count, "Pagination must terminate within the fixture's record count.");
                cursor = page.GetProperty("nextCursor").GetString();
            } while (cursor is not null);

            Assert.True(expected.SetEquals(seen));
            await VerifyReadPerformanceAsync(client, expected.First(), recordCount);
            Assert.Equal(before, await DatabaseSnapshot.CaptureAsync(connectionString));
        }
    }

    private static async Task VerifyReadPerformanceAsync(McpClient client, Guid id, int recordCount)
    {
        var operations = new (string Tool, Dictionary<string, object?> Arguments)[]
        {
            ("ngb_list_records", new() { ["kind"] = "catalog", ["typeCode"] = CheckpointDefinitions.Code, ["limit"] = 100 }),
            ("ngb_get_record", new() { ["kind"] = "catalog", ["typeCode"] = CheckpointDefinitions.Code, ["id"] = id }),
            ("ngb_search", new() { ["query"] = "Scale checkpoint", ["limit"] = 50 }),
            ("custom_current_user", new())
        };
        foreach (var (tool, arguments) in operations)
        {
            foreach (var concurrency in new[] { 1, 4 })
            {
                for (var batch = 0; batch < PerformanceBudgets.GetProperty("samplesPerScenario").GetInt32() / concurrency; batch++)
                {
                    await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
                    {
                        using var probe = new McpQueryProbe($"{tool}-records-{recordCount}-concurrency-{concurrency}");
                        var result = await CallAsync(client, tool, arguments);
                        Assert.Equal(JsonValueKind.Object, result.ValueKind);
                        Assert.InRange(probe.CommandCount, 1, PerformanceBudgets.GetProperty("maxSqlCommands").GetProperty(tool).GetInt32());
                    }));
                }
            }
        }
    }
}
