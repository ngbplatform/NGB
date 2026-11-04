using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class CatalogAcceptance
{
    private const string Path = "/api/catalogs/example.equipment_category";

    [Fact]
    public async Task Both_administrator_kinds_create_read_and_update_the_new_catalog_without_manual_grants()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode,
            "Renamed equipment supervisors");
        foreach (var username in new[] { "administrator", "application-administrator" })
        {
            using var admin = await host.CreateClientAsync(username);
            var created = await PostAsync<CatalogItemDto>(admin, Path, Payload(("code", username), ("name", "Calibration")));
            Assert.NotEqual(Guid.Empty, created.Id);
            var path = $"{Path}/{created.Id}";
            var saved = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
            Assert.Equal("Calibration", saved!.Display);
            Assert.Equal(username, saved.Payload.Fields!["code"].GetString());
            Assert.Equal("Calibration", saved.Payload.Fields["name"].GetString());

            using var update = await admin.PutAsJsonAsync(path, Payload(("name", "Calibrated tools")));
            await EnsureSuccessAsync(update);
            var updated = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
            Assert.Equal("Calibrated tools", updated!.Display);
            Assert.Equal(username, updated.Payload.Fields!["code"].GetString());
            Assert.Equal("Calibrated tools", updated.Payload.Fields["name"].GetString());
            var lookup = await admin.GetFromJsonAsync<LookupItemDto[]>(Path + "/lookup?q=Calibrated&limit=10", Json);
            Assert.Contains(lookup!, item => item.Id == created.Id && item.Label == "Calibrated tools");
        }
    }

    [Fact]
    public async Task Required_fields_and_length_limits_reject_invalid_create_and_partial_update()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var created = await PostAsync<CatalogItemDto>(admin, Path, Payload(("code", new string('c', 64)), ("name", new string('n', 200))));
        var path = $"{Path}/{created.Id}";
        var saved = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.Equal(new string('c', 64), saved!.Payload.Fields!["code"].GetString());
        Assert.Equal(new string('n', 200), saved.Payload.Fields["name"].GetString());
        var before = await admin.GetStringAsync(path);
        foreach (var field in new[] { "code", "name" })
        {
            var other = field == "code" ? "name" : "code";
            using var missing = await admin.PostAsJsonAsync(Path, Payload((other, "Valid")));
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            foreach (var invalid in new[] { "", " \t ", new string('x', field == "code" ? 65 : 201) })
            {
                using var create = await admin.PostAsJsonAsync(Path, Payload((field, invalid), (other, "Valid")));
                using var update = await admin.PutAsJsonAsync(path, Payload((field, invalid)));
                Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
                Assert.Equal(before, await admin.GetStringAsync(path));
            }
        }
    }

    [Fact]
    public async Task Duplicate_code_is_refused_on_create_and_update_without_changing_existing_records()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var first = await PostAsync<CatalogItemDto>(admin, Path, Payload(("code", "TOOLS"), ("name", "Tools")));
        var second = await PostAsync<CatalogItemDto>(admin, Path, Payload(("code", "METERS"), ("name", "Meters")));
        var firstBefore = await admin.GetStringAsync($"{Path}/{first.Id}");
        var secondBefore = await admin.GetStringAsync($"{Path}/{second.Id}");

        using var duplicate = await admin.PostAsJsonAsync(Path, Payload(("code", "TOOLS"), ("name", "Duplicate")));
        using var update = await admin.PutAsJsonAsync($"{Path}/{second.Id}", Payload(("code", "TOOLS")));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(firstBefore, await admin.GetStringAsync($"{Path}/{first.Id}"));
        Assert.Equal(secondBefore, await admin.GetStringAsync($"{Path}/{second.Id}"));
    }

    [Fact]
    public async Task Concurrent_creates_cannot_commit_the_same_code_twice()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(index =>
            admin.PostAsJsonAsync(Path, Payload(("code", "CONCURRENT"), ("name", "Concurrent " + index)))));
        try
        {
            var success = Assert.Single(responses, response => response.IsSuccessStatusCode);
            var rejected = Assert.Single(responses, response => !response.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            var created = await success.Content.ReadFromJsonAsync<CatalogItemDto>(Json);
            var lookup = await admin.GetFromJsonAsync<LookupItemDto[]>(Path + "/lookup?q=Concurrent&limit=10", Json);
            Assert.Equal(created!.Id, Assert.Single(lookup!).Id);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Reader_inactive_and_display_name_only_users_cannot_read_or_mutate_the_catalog()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode, "Administrator");
        await host.LinkUserAsync("imitator", true, "equipment.observer", "Administrator");
        using var admin = await host.CreateClientAsync("administrator");
        var created = await PostAsync<CatalogItemDto>(admin, Path, Payload(("code", "PRIVATE"), ("name", "Protected category")));
        var path = $"{Path}/{created.Id}";
        var before = await admin.GetStringAsync(path);
        foreach (var username in new[] { "reader", "inactive-administrator", "imitator" })
        {
            using var limited = await host.CreateClientAsync(username);
            using var read = await limited.GetAsync(path);
            using var create = await limited.PostAsJsonAsync(Path, Payload(("code", "DENIED"), ("name", "Denied")));
            using var update = await limited.PutAsJsonAsync(path, Payload(("name", "Denied")));
            using var deletion = await limited.PostAsJsonAsync(path + "/mark-for-deletion", new { });
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deletion.StatusCode);
            Assert.DoesNotContain("Protected category", await read.Content.ReadAsStringAsync());
            Assert.Equal(before, await admin.GetStringAsync(path));
        }

        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync(Path, Payload(("code", "ANON"), ("name", "Anonymous")));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task Repeated_public_migration_execution_preserves_saved_category_and_uniqueness()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var created = await PostAsync<CatalogItemDto>(admin, Path, Payload(("code", "PERSISTED"), ("name", "Persisted category")));
        var path = $"{Path}/{created.Id}";
        var before = await admin.GetStringAsync(path);

        await RunMigratorAsync(host.ConnectionString);
        await RunMigratorAsync(host.ConnectionString);

        Assert.Equal(before, await admin.GetStringAsync(path));
        using var duplicate = await admin.PostAsJsonAsync(Path, Payload(("code", "PERSISTED"), ("name", "Duplicate")));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private static async Task RunMigratorAsync(string connectionString)
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configurationDirectory = frameworkDirectory.Parent!;
        var stage = configurationDirectory.Parent!.Parent!.Parent!;
        var directory = System.IO.Path.Combine(stage.FullName, "application", "EquipmentInspection.Migrator", "bin",
            configurationDirectory.Name, frameworkDirectory.Name);
        var assembly = System.IO.Path.Combine(directory, "EquipmentInspection.Migrator.dll");
        Assert.True(File.Exists(assembly), "The independent runner must build the Migrator in its own application output.");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory
        };
        foreach (var argument in new[] { assembly, "--connection=" + connectionString,
                     "--lock-timeout-seconds=10", "--statement-timeout-seconds=10", "--schema-lock-wait-seconds=10" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, $"Migrator failed: {await output}\n{await error}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
