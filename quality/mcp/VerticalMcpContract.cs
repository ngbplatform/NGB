using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExternalMcp.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using NGB.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Effects;
using NGB.Contracts.Notes;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Persistence.Attachments;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using Xunit;

namespace NGB.Testing.Mcp;

internal static class VerticalMcpContract
{
    internal static TimeProvider Clock { get; } = new FixedClock();

    internal static async Task VerifyAsync<TProgram>(string connectionString, string prefix, bool contentEnabled = false) where TProgram : class
    {
        await using var identity = new KeycloakFixture();
        await identity.StartAsync();
        var (token, subject) = await identity.AuthenticateUserAsync();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var origin = $"http://127.0.0.1:{port}";
        using var environment = new EnvironmentScope(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["ConnectionStrings__DefaultConnection"] = connectionString,
            ["KeycloakSettings__Issuer"] = identity.Issuer,
            ["KeycloakSettings__RequireHttpsMetadata"] = "false",
            ["KeycloakSettings__ClientIds__0"] = KeycloakFixture.Audience,
            ["FeatureManagement__Notes"] = contentEnabled.ToString(),
            ["FeatureManagement__Attachments"] = contentEnabled.ToString(),
            ["Attachments__MinIO__InternalEndpoint"] = origin,
            ["Attachments__MinIO__PublicEndpoint"] = origin,
            ["Attachments__MinIO__AllowInsecureHttp"] = "true",
            ["Attachments__MinIO__AccessKey"] = "mcp-metadata-fixture",
            ["Attachments__MinIO__SecretKey"] = Guid.NewGuid().ToString("N"),
            ["Serilog__WriteTo__1__Args__serverUrl"] = "http://127.0.0.1:5341",
            ["ExternalLinksSettings__HealthUiUrl"] = "https://example.invalid/health",
            ["ExternalLinksSettings__BackgroundJobsUiUrl"] = "https://example.invalid/jobs",
            ["Mcp__Enabled"] = "true",
            ["Mcp__Resource"] = origin + "/mcp",
            ["Mcp__Audience"] = KeycloakFixture.Audience,
            ["Mcp__AllowLocalhostHttp"] = "true",
            ["Mcp__RequestsPerMinute"] = "10000"
        });
        await using var factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock)));
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        factory.StartServer();
        using var http = new HttpClient { BaseAddress = new Uri(origin) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(origin + "/mcp") }, http));
        var tools = await client.ListToolsAsync();
        Assert.Equal(contentEnabled ? 18 : 15, tools.Count);
        Assert.DoesNotContain(tools, tool => tool.Name is "ngb_search_platform_knowledge" or "ngb_get_platform_knowledge");
        Assert.DoesNotContain(tools, tool => tool.Name.Contains("register", StringComparison.Ordinal));
        Assert.Equal(contentEnabled, tools.Any(tool => tool.Name == "ngb_list_notes"));
        Assert.Equal(contentEnabled, tools.Any(tool => tool.Name == "ngb_list_attachments"));

        var metadata = await CallAsync(client, "ngb_list_metadata", new() { ["limit"] = 100 });
        var installed = metadata.GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(installed, item => item.GetProperty("kind").GetString() == "catalog"
            && item.GetProperty("typeCode").GetString()!.StartsWith(prefix, StringComparison.Ordinal));
        Assert.Contains(installed, item => item.GetProperty("kind").GetString() == "document"
            && item.GetProperty("typeCode").GetString()!.StartsWith(prefix, StringComparison.Ordinal));
        var snapshot = await DatabaseSnapshot.CaptureAsync(connectionString);
        var performance = new List<McpReadOperation>();
        var records = new List<(string Kind, string Code, Guid Id, string Title)>();
        foreach (var item in installed.Where(item => item.GetProperty("typeCode").GetString()!.StartsWith(prefix, StringComparison.Ordinal)))
        {
            var kind = item.GetProperty("kind").GetString();
            var code = item.GetProperty("typeCode").GetString();
            await CallAsync(client, "ngb_get_metadata", new() { ["kind"] = kind, ["typeCode"] = code });
            if (kind is "catalog" or "document")
            {
                var page = (await CallAsync(client, "ngb_list_records", new() { ["kind"] = kind, ["typeCode"] = code, ["limit"] = 2 }))
                    .GetProperty(kind + "s");
                var items = page.GetProperty("items").EnumerateArray().ToArray();
                foreach (var record in items.Take(1))
                {
                    var id = record.GetProperty("id").GetGuid();
                    var title = record.GetProperty("display").GetString()!;
                    records.Add((kind, code!, id, title));
                    var detail = await CallAsync(client, "ngb_get_record", new() { ["kind"] = kind, ["typeCode"] = code, ["id"] = id });
                    using var restDetailResponse = await http.GetAsync($"/api/{kind}s/{Uri.EscapeDataString(code!)}/{id:D}");
                    restDetailResponse.EnsureSuccessStatusCode();
                    var restDetail = await restDetailResponse.Content.ReadFromJsonAsync<JsonElement>();
                    var normalization = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                    normalization.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                    var restRecord = kind == "catalog"
                        ? JsonSerializer.SerializeToElement(restDetail.Deserialize<CatalogItemDto>(normalization), normalization)
                        : JsonSerializer.SerializeToElement(restDetail.Deserialize<DocumentDto>(normalization), normalization);
                    Assert.True(JsonElement.DeepEquals(restRecord, detail.GetProperty(kind)), $"{code}: REST/MCP detail differs.");
                    var fetched = await client.CallToolAsync("fetch", new Dictionary<string, object?> { ["id"] = $"{kind}:{code}:{id:D}" });
                    Assert.NotEqual(true, fetched.IsError);
                    Assert.Equal(title, fetched.StructuredContent!.Value.GetProperty("title").GetString());
                    if (kind == "document")
                        await VerifyDocumentAsync(client, http, code!, id);
                }
                if (page.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind == JsonValueKind.String)
                {
                    var continuation = (await CallAsync(client, "ngb_list_records", new()
                    {
                        ["kind"] = kind, ["typeCode"] = code, ["limit"] = 2, ["cursor"] = cursor.GetString()
                    })).GetProperty(kind + "s");
                    var firstIds = items.Select(record => record.GetProperty("id").GetGuid()).ToHashSet();
                    Assert.DoesNotContain(continuation.GetProperty("items").EnumerateArray(), record => firstIds.Contains(record.GetProperty("id").GetGuid()));
                }
            }
        }
        Assert.Contains(records, record => record.Kind == "catalog");
        Assert.Contains(records, record => record.Kind == "document");
        var reports = await CallAsync(client, "ngb_list_reports", new() { ["limit"] = 100 });
        var codes = reports.GetProperty("items").EnumerateArray().Select(report => report.GetProperty("reportCode").GetString()).Order().ToArray();
        Assert.NotEmpty(codes);
        using var restResponse = await http.GetAsync("/api/report-definitions");
        restResponse.EnsureSuccessStatusCode();
        var rest = await restResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(codes, rest.EnumerateArray().Select(report => report.GetProperty("reportCode").GetString()).Order());
        foreach (var code in codes)
            await CallAsync(client, "ngb_get_report_definition", new() { ["reportCode"] = code });

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var executed = 0;
        var verticalReports = reports.GetProperty("items").EnumerateArray()
                     .Where(item => item.GetProperty("reportCode").GetString()!.StartsWith(prefix, StringComparison.Ordinal))
                     .Where(item => !item.TryGetProperty("filters", out var filters) || filters.ValueKind != JsonValueKind.Array
                         || !filters.EnumerateArray().Any(filter => filter.GetProperty("isRequired").GetBoolean())).Take(2);
        var canonicalReports = reports.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("reportCode").GetString() == "accounting.trial_balance");
        foreach (var definition in verticalReports.Concat(canonicalReports))
        {
            var code = definition.GetProperty("reportCode").GetString()!;
            var parameters = new Dictionary<string, string>();
            if (definition.TryGetProperty("parameters", out var declared) && declared.ValueKind == JsonValueKind.Array)
            {
                foreach (var parameter in declared.EnumerateArray())
                {
                    var parameterCode = parameter.GetProperty("code").GetString()!;
                    Assert.Equal("date", parameter.GetProperty("dataType").GetString());
                    parameters[parameterCode] = parameterCode.Contains("from", StringComparison.Ordinal) ? "2026-01-01" : "2026-10-01";
                }
            }
            var request = new ReportExecutionRequestDto(Parameters: parameters, Limit: 2);
            using var executedRest = await http.PostAsJsonAsync($"/api/reports/{Uri.EscapeDataString(code)}/execute", request, jsonOptions);
            Assert.True(executedRest.IsSuccessStatusCode,
                $"REST report {code}: {(int)executedRest.StatusCode} {await executedRest.Content.ReadAsStringAsync()}");
            var restResult = await executedRest.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(jsonOptions);
            var mcpResult = (await CallAsync(client, "ngb_execute_report", new() { ["reportCode"] = code, ["request"] = request }))
                .Deserialize<ReportExecutionResponseDto>(jsonOptions)!;
            Assert.Equal(JsonSerializer.Serialize(restResult!.Sheet, jsonOptions), JsonSerializer.Serialize(mcpResult.Sheet, jsonOptions));
            Assert.Equal(restResult.HasMore, mcpResult.HasMore);
            Assert.Equal(restResult.Total, mcpResult.Total);
            performance.Add(new("report-" + code, "ngb_execute_report", new() { ["reportCode"] = code, ["request"] = request },
                $"/api/reports/{Uri.EscapeDataString(code)}/execute", request));
            executed++;
        }
        Assert.True(executed > 0, "At least one actual vertical report must be executed through REST and MCP.");
        if (prefix == "crm.")
            Assert.DoesNotContain(codes, code => code!.StartsWith("accounting.", StringComparison.Ordinal));
        await CallAsync(client, "ngb_search", new() { ["query"] = "test", ["limit"] = 2 });
        var searchable = records.First(record => record.Kind == "catalog");
        var found = await CallAsync(client, "ngb_search", new() { ["query"] = searchable.Title, ["limit"] = 50 });
        Assert.Contains(found.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("recordId").ValueKind == JsonValueKind.String && item.GetProperty("recordId").GetGuid() == searchable.Id);
        if (!contentEnabled)
        {
            var catalogPath = $"/api/catalogs/{Uri.EscapeDataString(searchable.Code)}";
            performance.Add(new("catalog-list", "ngb_list_records", new()
            {
                ["kind"] = "catalog", ["typeCode"] = searchable.Code, ["limit"] = 100
            }, catalogPath + "?limit=100&includeTotal=false"));
            performance.Add(new("catalog-detail", "ngb_get_record", new()
            {
                ["kind"] = "catalog", ["typeCode"] = searchable.Code, ["id"] = searchable.Id
            }, catalogPath + $"/{searchable.Id:D}"));
            var document = records.First(record => record.Kind == "document");
            var documentPath = $"/api/documents/{Uri.EscapeDataString(document.Code)}/{document.Id:D}";
            performance.Add(new("document-flow", "ngb_get_document_flow", new()
            {
                ["typeCode"] = document.Code, ["id"] = document.Id, ["depth"] = 2, ["maxNodes"] = 50
            }, documentPath + "/graph?depth=2&maxNodes=50"));
            performance.Add(new("document-effects", "ngb_get_document_effects", new()
            {
                ["typeCode"] = document.Code, ["id"] = document.Id, ["limit"] = 100
            }, documentPath + "/effects?limit=100"));
            await VerticalMcpPerformance.VerifyAsync(prefix, client, http, performance);
        }
        Assert.Equal(snapshot, await DatabaseSnapshot.CaptureAsync(connectionString));
        if (contentEnabled)
            await VerifyContentAsync(factory.Services, client, http, subject, records, connectionString);
        await VerifyRestrictedActorAsync(factory.Services, client, identity, origin, records, connectionString, contentEnabled);
    }

    private static async Task VerifyDocumentAsync(McpClient client, HttpClient http, string code, Guid id)
    {
        var startedAt = DateTime.UtcNow;
        var effects = await CallAsync(client, "ngb_get_document_effects", new() { ["typeCode"] = code, ["id"] = id, ["limit"] = 100 });
        using var restEffectsResponse = await http.GetAsync($"/api/documents/{Uri.EscapeDataString(code)}/{id:D}/effects?limit=100");
        restEffectsResponse.EnsureSuccessStatusCode();
        var restEffects = await restEffectsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var finishedAt = DateTime.UtcNow;
        Assert.True(JsonElement.DeepEquals(
            NormalizeEffects(restEffects, code, startedAt, finishedAt),
            NormalizeEffects(effects.GetProperty("effects"), code, startedAt, finishedAt)), $"{code}: REST/MCP Effects differ.");
        Assert.Equal("result_local", effects.GetProperty("identifierScope").GetString());
        if (code == "pm.lease")
        {
            Assert.Empty(effects.GetProperty("effects").GetProperty("accountingEntries").EnumerateArray());
            Assert.Empty(effects.GetProperty("effects").GetProperty("operationalRegisterMovements").EnumerateArray());
            Assert.Equal("complete", effects.GetProperty("completeness").GetString());
        }
        var flow = await CallAsync(client, "ngb_get_document_flow", new() { ["typeCode"] = code, ["id"] = id, ["depth"] = 2, ["maxNodes"] = 50 });
        Assert.NotEmpty(flow.GetProperty("graph").GetProperty("nodes").EnumerateArray());
        var history = await CallAsync(client, "ngb_get_audit_history", new() { ["kind"] = "document", ["id"] = id, ["limit"] = 2 });
        Assert.NotEmpty(history.GetProperty("items").EnumerateArray());
    }

    private static JsonElement NormalizeEffects(JsonElement value, string code, DateTime startedAt, DateTime finishedAt)
    {
        var volatileRegisters = code switch
        {
            "trd.item_price_update" => new[] { "trd.item_prices" },
            "crm.lead_intake" or "crm.lead_qualification" => ["crm.lead_funnel"],
            "crm.lead_conversion" => ["crm.lead_funnel", "crm.opportunities"],
            "crm.opportunity_update" => ["crm.opportunities"],
            "crm.quote" => ["crm.quotes"],
            "crm.activity_log" => ["crm.activities"],
            _ => []
        };
        if (volatileRegisters.Length == 0)
            return value;

        // Existing Trade and CRM posting handlers stamp DateTime.UtcNow when rebuilding
        // current Effects. Verify that timestamp's semantics separately from stable data.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var effects = value.Deserialize<DocumentEffectsDto>(options)!;
        var writes = effects.ReferenceRegisterWrites.Select(row =>
        {
            Assert.Contains(row.RegisterCode, volatileRegisters);
            var updatedAt = row.Fields["updated_at_utc"].GetDateTime();
            Assert.InRange(updatedAt, startedAt, finishedAt);
            return row with { Fields = row.Fields.Where(field => field.Key != "updated_at_utc").ToDictionary() };
        }).ToArray();
        return JsonSerializer.SerializeToElement(effects with { ReferenceRegisterWrites = writes }, options);
    }

    private static async Task VerifyRestrictedActorAsync(IServiceProvider services, McpClient admin, KeycloakFixture identity,
        string origin, IReadOnlyList<(string Kind, string Code, Guid Id, string Title)> records, string connectionString, bool contentEnabled)
    {
        var (token, subject) = await identity.AuthenticateUserAsync("reader");
        await using (var scope = services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>()
                .UpsertAsync(subject, "reader@example.test", "Restricted MCP reader", true);
            await uow.CommitAsync();
        }
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await using var reader = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(origin + "/mcp") }, http));
        var before = await DatabaseSnapshot.CaptureAsync(connectionString);
        Assert.Empty((await CallAsync(reader, "ngb_list_metadata", new())).GetProperty("items").EnumerateArray());
        Assert.Empty((await CallAsync(reader, "ngb_list_reports", new())).GetProperty("items").EnumerateArray());
        foreach (var record in new[] { records.First(item => item.Kind == "catalog"), records.First(item => item.Kind == "document") })
        {
            var request = new Dictionary<string, object?> { ["kind"] = record.Kind, ["typeCode"] = record.Code, ["id"] = record.Id };
            var denied = await reader.CallToolAsync("ngb_get_record", request);
            Assert.True(denied.IsError);
            var fetched = await reader.CallToolAsync("fetch", new Dictionary<string, object?> { ["id"] = $"{record.Kind}:{record.Code}:{record.Id:D}" });
            Assert.True(fetched.IsError);
            Assert.DoesNotContain(record.Title, JsonSerializer.Serialize(fetched.Content));
            await CallAsync(admin, "ngb_get_record", request);
        }
        var document = records.First(item => item.Kind == "document");
        foreach (var tool in new[] { "ngb_get_document_flow", "ngb_get_document_effects" })
        {
            var denied = await reader.CallToolAsync(tool, new Dictionary<string, object?> { ["typeCode"] = document.Code, ["id"] = document.Id });
            Assert.True(denied.IsError);
        }
        var audit = await reader.CallToolAsync("ngb_get_audit_history", new Dictionary<string, object?> { ["kind"] = "document", ["id"] = document.Id });
        Assert.True(audit.IsError);
        if (contentEnabled)
        {
            var target = new { kind = "document", typeCode = document.Code, id = document.Id };
            foreach (var tool in new[] { "ngb_list_notes", "ngb_list_attachments", "ngb_get_content_summary" })
            {
                var denied = await reader.CallToolAsync(tool, new Dictionary<string, object?> { ["target"] = target });
                Assert.True(denied.IsError);
            }
        }
        await CallAsync(reader, "ngb_get_work_items", new() { ["summaryOnly"] = true });
        Assert.Equal(before, await DatabaseSnapshot.CaptureAsync(connectionString));
    }

    private static async Task VerifyContentAsync(IServiceProvider services, McpClient client, HttpClient http, string subject,
        IReadOnlyList<(string Kind, string Code, Guid Id, string Title)> records, string connectionString)
    {
        var record = records.First(item => item.Kind == "document");
        var target = new BusinessObjectRef(BusinessObjectKind.Document, record.Code, record.Id);
        var readyId = Guid.NewGuid();
        Guid userId;
        await using (var scope = services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            userId = await scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>()
                .UpsertAsync(subject, "alex@example.test", "Alex Carter", true);
            var repository = scope.ServiceProvider.GetRequiredService<IAttachmentRepository>();
            var now = DateTime.UtcNow;
            foreach (var status in new[] { AttachmentStatus.Ready, AttachmentStatus.PendingUpload, AttachmentStatus.Deleted })
            {
                await repository.InsertAsync(new AttachmentRecord(status == AttachmentStatus.Ready ? readyId : Guid.NewGuid(),
                    target, "fixture.pdf", "application/pdf", 128, "private-object-" + status, "private-upload-" + status, status,
                    now, userId, now.AddHours(1), status == AttachmentStatus.Ready ? now : null,
                    status == AttachmentStatus.Deleted ? now : null,
                    status == AttachmentStatus.Deleted ? userId : null), default);
            }
            await uow.CommitAsync();
        }
        using var created = await http.PostAsJsonAsync("/api/notes", new CreateNoteRequest(target, "MCP business note"));
        created.EnsureSuccessStatusCode();
        var note = await created.Content.ReadFromJsonAsync<NoteDto>();
        var request = new Dictionary<string, object?>
        {
            ["target"] = new { kind = "document", typeCode = record.Code, id = record.Id },
            ["limit"] = 1
        };
        var before = await DatabaseSnapshot.CaptureAsync(connectionString);
        var notes = await CallAsync(client, "ngb_list_notes", request);
        Assert.Equal(note!.Id, notes.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal("MCP business note", notes.GetProperty("items")[0].GetProperty("text").GetString());
        var attachments = await CallAsync(client, "ngb_list_attachments", request);
        Assert.Equal(readyId, Assert.Single(attachments.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.DoesNotContain("private-", attachments.GetRawText());
        Assert.DoesNotContain("url", attachments.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var summary = await CallAsync(client, "ngb_get_content_summary", new() { ["target"] = request["target"] });
        Assert.Equal(1, summary.GetProperty("attachments").GetInt32());
        Assert.Equal(1, summary.GetProperty("notes").GetInt32());
        await CallAsync(client, "ngb_get_work_items", new());
        Assert.Equal(before, await DatabaseSnapshot.CaptureAsync(connectionString));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 4, 5, 12, 0, 0, TimeSpan.Zero);
    }

    private static async Task<JsonElement> CallAsync(McpClient client, string name, Dictionary<string, object?> arguments)
    {
        var response = await client.CallToolAsync(name, arguments);
        Assert.True(response.IsError != true, $"{name}: {JsonSerializer.Serialize(response.Content)}");
        return response.StructuredContent!.Value.GetProperty("data");
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

        internal EnvironmentScope(IReadOnlyDictionary<string, string?> configuration)
        {
            foreach (var (name, value) in configuration)
            {
                _original.Add(name, Environment.GetEnvironmentVariable(name));
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _original)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
