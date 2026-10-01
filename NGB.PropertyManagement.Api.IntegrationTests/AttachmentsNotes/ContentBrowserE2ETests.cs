using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.Testing.Containers;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.AttachmentsNotes;

/// <summary>Real API + Keycloak + PostgreSQL + MinIO + Chromium; no content API responses are mocked.</summary>
[Collection(PmIntegrationCollection.Name)]
public sealed class ContentBrowserE2ETests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Browser_upload_download_notes_and_delete_do_not_mutate_catalog_or_journal()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "NGB.sln"))) root = root.Parent;
        await using var image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(Path.Combine(root!.FullName, "docker/minio"))
            .WithName("ngb-minio-dev:2025-10-15").WithDeleteIfExists(false).WithCleanUp(false).Build();
        await image.CreateAsync();
        await using var minio = new ContainerBuilder("ngb-minio-dev:2025-10-15")
            .WithEnvironment("MINIO_ROOT_USER", "browser-test")
            .WithEnvironment("MINIO_ROOT_PASSWORD", "isolated-test-secret")
            .WithEnvironment("MINIO_API_CORS_ALLOW_ORIGIN", "*").WithPortBinding(9000, true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/minio/health/live"))).Build();
        await using (await TestcontainerStartupGate.AcquireAsync()) await minio.StartAsync();
        var endpoint = $"http://127.0.0.1:{minio.GetMappedPublicPort(9000)}";
        using var admin = new MinioClient().WithEndpoint("127.0.0.1", minio.GetMappedPublicPort(9000))
            .WithCredentials("browser-test", "isolated-test-secret").Build();
        await admin.MakeBucketAsync(new MakeBucketArgs().WithBucket("browser-attachments"));
        await using var factory = new PmApiFactory(fixture, new Dictionary<string, string?>
        {
            ["Attachments:MinIO:InternalEndpoint"] = endpoint, ["Attachments:MinIO:PublicEndpoint"] = endpoint,
            ["Attachments:MinIO:AccessKey"] = "browser-test", ["Attachments:MinIO:SecretKey"] = "isolated-test-secret",
            ["Attachments:MinIO:Bucket"] = "browser-attachments", ["Attachments:MinIO:AllowInsecureHttp"] = "true"
        });
        factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses
            .Single();
        client.BaseAddress = new Uri(address);
        using var catalogResponse = await client.PostAsJsonAsync("/api/catalogs/pm.party",
            new { fields = new { display = "Attachment browser fixture" } });
        catalogResponse.EnsureSuccessStatusCode();
        var catalog = await catalogResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var journalResponse = await client.PostAsJsonAsync("/api/accounting/general-journal-entries",
            new { dateUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) });
        journalResponse.EnsureSuccessStatusCode();
        var journal = await journalResponse.Content.ReadFromJsonAsync<JsonElement>();
        var catalogId = catalog.GetProperty("id").GetGuid();
        var journalId = journal.GetProperty("document").GetProperty("id").GetGuid();
        var paths = new[] { $"/catalogs/pm.party/{catalogId}", $"/accounting/general-journal-entries/{journalId}" };
        var before = new[]
            { await client.GetStringAsync("/api" + paths[0]), await client.GetStringAsync("/api" + paths[1]) };
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = Path.Combine(root.FullName, "ui"), RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add("tests/e2e/support/contentRealStack.mjs");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            api = address, token = client.DefaultRequestHeaders.Authorization!.Parameter, storage = endpoint, paths
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        process.ExitCode.Should().Be(0, $"browser scenario failed: {await output}\n{await errors}");
        (await client.GetStringAsync("/api" + paths[0])).Should().Be(before[0]);
        (await client.GetStringAsync("/api" + paths[1])).Should().Be(before[1]);
    }
}
