using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;
using Xunit;

namespace NGB.Testing.Mcp;

internal sealed record McpReadOperation(
    string Name,
    string Tool,
    Dictionary<string, object?> Arguments,
    string RestPath,
    object? RestBody = null);

internal static class VerticalMcpPerformance
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    internal static async Task VerifyAsync(string prefix, McpClient client, HttpClient http, IReadOnlyList<McpReadOperation> operations)
    {
        var directory = Environment.GetEnvironmentVariable("NGB_MCP_PERFORMANCE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var budgets = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "mcp-vertical-performance-budgets.json")));
        var samples = budgets.GetProperty("samplesPerScenario").GetInt32();
        var evidence = new List<object>();
        foreach (var operation in operations)
        {
            // Warm both paths before comparing transport overhead on the same host and data.
            for (var warmup = 0; warmup < 5; warmup++)
            {
                await ReadAsync(false, 1);
                await ReadAsync(true, 1);
            }

            foreach (var concurrency in new[] { 1, 4 })
            {
                var rest = new List<Sample>();
                var mcp = new List<Sample>();
                var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                for (var batch = 0; batch < samples / concurrency; batch++)
                {
                    rest.AddRange(await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => ReadAsync(false, concurrency))));
                    mcp.AddRange(await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => ReadAsync(true, concurrency))));
                }

                var restP95 = Percentile(rest, 0.95);
                var mcpP95 = Percentile(mcp, 0.95);
                Assert.True(mcp.Max(sample => sample.Queries) <= rest.Max(sample => sample.Queries)
                    + budgets.GetProperty("maxAdditionalSqlCommands").GetInt32(),
                    $"{prefix}{operation.Name}: MCP exceeded the REST-relative SQL budget.");
                Assert.True(mcpP95 <= restP95 * budgets.GetProperty("maxP95RatioToRest").GetDouble(),
                    $"{prefix}{operation.Name}: MCP p95 {mcpP95:F2} ms exceeds the measured REST baseline {restP95:F2} ms.");
                Assert.All(mcp, sample => Assert.InRange(sample.Bytes, 1, budgets.GetProperty("maxStructuredResponseBytes").GetInt32()));
                evidence.Add(new
                {
                    scenario = prefix + operation.Name,
                    concurrency,
                    samples,
                    rest = Summarize(rest),
                    mcp = Summarize(mcp),
                    allocatedBytesForBothWorkloads = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore
                });
            }

            async Task<Sample> ReadAsync(bool useMcp, int concurrency)
            {
                using var probe = new McpQueryProbe($"{prefix}{operation.Name}-{(useMcp ? "mcp" : "rest")}-concurrency-{concurrency}");
                var watch = Stopwatch.StartNew();
                string payload;
                if (useMcp)
                {
                    var result = await client.CallToolAsync(operation.Tool, operation.Arguments);
                    Assert.True(result.IsError != true, $"{operation.Tool}: {JsonSerializer.Serialize(result.Content)}");
                    payload = result.StructuredContent!.Value.GetRawText();
                }
                else
                {
                    using var response = operation.RestBody is null
                        ? await http.GetAsync(operation.RestPath)
                        : await http.PostAsJsonAsync(operation.RestPath, operation.RestBody, JsonOptions);
                    response.EnsureSuccessStatusCode();
                    payload = await response.Content.ReadAsStringAsync();
                }
                Assert.True(probe.CommandCount > 0, "The trace probe must observe database work across HTTP.");
                return new Sample(watch.Elapsed.TotalMilliseconds, probe.CommandCount, System.Text.Encoding.UTF8.GetByteCount(payload));
            }
        }

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, prefix.TrimEnd('.') + "-comparison.json"),
            JsonSerializer.Serialize(new { status = "passed", evidence }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Summarize(IReadOnlyList<Sample> samples)
        => new
        {
            medianMs = Percentile(samples, 0.5),
            p95Ms = Percentile(samples, 0.95),
            p99Ms = Percentile(samples, 0.99),
            maxSqlCommands = samples.Max(sample => sample.Queries),
            maxStructuredResponseBytes = samples.Max(sample => sample.Bytes)
        };

    private static double Percentile(IReadOnlyList<Sample> samples, double percentile)
        => samples.Select(sample => sample.Milliseconds).Order().ElementAt((int)Math.Ceiling(samples.Count * percentile) - 1);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record Sample(double Milliseconds, int Queries, int Bytes);
}
