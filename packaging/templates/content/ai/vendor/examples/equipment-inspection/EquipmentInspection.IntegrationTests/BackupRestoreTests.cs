using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EquipmentInspection.Definitions;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class BackupRestoreTests(ITestOutputHelper output)
{
    [Fact]
    public async Task A_separate_restored_application_reopens_the_original_posted_document()
    {
        await using var source = new InspectionHost();
        await source.StartAsync();
        using var writer = await source.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(writer, "/api/catalogs/example.equipment",
            Payload(("display", "Backup fixture")));
        var draft = await PostAsync<DocumentDto>(writer, "/api/documents/" + EquipmentDefinitions.InspectionCode,
            CompleteInspectionPayload(equipment.Id));
        var path = "/api/documents/" + EquipmentDefinitions.InspectionCode + "/" + draft.Id;
        var editor = (await writer.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var posted = await ExecutePostAsync(writer, path, editor.DocumentVersion, "before-backup");
        await EnsureSuccessAsync(posted);
        var before = (await writer.GetFromJsonAsync<DocumentDto>(path, Json))!;
        var infrastructure = await TestInfrastructure.GetAsync();
        byte[]? backup = null;
        await using var restored = new InspectionHost(prepareDatabase: async connection =>
        {
            backup = await infrastructure.RestoreBackupAsync(source.ConnectionString, connection);
        });
        await restored.StartAsync();
        using var reader = await restored.CreateClientAsync("administrator");
        using var response = await reader.GetAsync(path);
        await EnsureSuccessAsync(response);
        var after = (await response.Content.ReadFromJsonAsync<DocumentDto>(Json))!;
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(JsonSerializer.Serialize(before.Payload), JsonSerializer.Serialize(after.Payload));
        Assert.Equal(DocumentStatus.Posted, after.Status);
        Assert.Equal(before.Number, after.Number);
        Assert.NotEmpty(backup!);

        output.WriteLine("NGB-CHECK " + JsonSerializer.Serialize(new
        {
            kind = "restore-rehearsal",
            target = "navigation:example.inspection",
            key = "posted-document",
            sourceInstance = new NpgsqlConnectionStringBuilder(source.ConnectionString).Database,
            restoredInstance = new NpgsqlConnectionStringBuilder(restored.ConnectionString).Database,
            backupBytes = backup!.Length,
            backupSha256 = Convert.ToHexStringLower(SHA256.HashData(backup)),
            readStatus = (int)response.StatusCode,
            recordId = after.Id,
            beforeBackup = new { before.Id, before.Number, before.Status, before.Payload },
            afterRestore = new { after.Id, after.Number, after.Status, after.Payload }
        }));
    }
}
