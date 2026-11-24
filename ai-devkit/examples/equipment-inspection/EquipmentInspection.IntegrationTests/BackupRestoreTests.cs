using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Runtime.OperationalRegisters;
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
        var infrastructure = await TestInfrastructure.GetAsync();
        await using var originalIdentityStore = await infrastructure.CreateDatabaseAsync();
        await using var restoredIdentityStore = await infrastructure.CreateDatabaseAsync();
        await using var originalServer = infrastructure.CreateIdentityServer(originalIdentityStore);
        await using var restoredServer = infrastructure.CreateIdentityServer(restoredIdentityStore);
        await using var originalStorage = new TestAttachmentStorage();
        await using var restoredStorage = new TestAttachmentStorage();
        await Task.WhenAll(originalServer.StartAsync(), originalStorage.StartAsync(), restoredStorage.StartAsync());
        await using var identity = new TestIdentity
        {
            ServerOrigin = $"http://{originalServer.Hostname}:{originalServer.GetMappedPublicPort(8080)}"
        };
        await identity.StartAsync();
        await using var source = new InspectionHost(configuration: originalStorage.Configuration, activeIdentity: identity);
        await source.StartAsync();
        using var writer = await source.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(writer, "/api/catalogs/example.equipment",
            Payload(("display", "Backup fixture")));
        const string documentType = EquipmentUsageDefinitions.DocumentCode;
        var draft = await PostAsync<DocumentDto>(writer, "/api/documents/" + documentType,
            Payload(("equipment_id", equipment.Id), ("hours", 6.25m)));
        var path = "/api/documents/" + documentType + "/" + draft.Id;
        var editor = (await writer.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var posted = await ExecutePostAsync(writer, path, editor.DocumentVersion, "before-backup");
        await EnsureSuccessAsync(posted);
        var before = (await writer.GetFromJsonAsync<DocumentDto>(path, Json))!;
        DateOnly month;
        var register = OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode);
        await using (var scope = source.Services.CreateAsyncScope())
        {
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var record = await documents.GetAsync(before.Id);
            Assert.NotNull(record);
            month = new DateOnly(record.DateUtc.Year, record.DateUtc.Month, 1);
            var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
            Assert.Equal(1, await maintenance.FinalizeRegisterDirtyAsync(register));
        }
        var originalBalance = await ReadBalanceAsync(source, register, month);
        Assert.Equal(6.25m, originalBalance);
        const string accessPath = "/api/security/me/access";
        var originalAccess = await writer.GetFromJsonAsync<JsonElement>(accessPath, Json);
        var originalSubject = originalAccess.GetProperty("authSubject").GetString();
        Assert.False(string.IsNullOrWhiteSpace(originalSubject));
        var bytes = "Approved inspection record"u8.ToArray();
        var upload = await AttachmentsHttpTests.RequestUploadAsync(writer,
            new BusinessObjectRef(BusinessObjectKind.Document, documentType, draft.Id), bytes.Length);
        await AttachmentsHttpTests.UploadAsync(upload, bytes);
        using var complete = await writer.PostAsync($"/api/attachments/{upload.AttachmentId}/complete", null);
        await EnsureSuccessAsync(complete);

        var objects = await originalStorage.BackupAsync();
        await restoredStorage.RestoreAsync(objects);
        await infrastructure.RestoreBackupAsync(originalIdentityStore.ConnectionString, restoredIdentityStore.ConnectionString);
        await restoredServer.StartAsync();
        identity.ServerOrigin = $"http://{restoredServer.Hostname}:{restoredServer.GetMappedPublicPort(8080)}";
        byte[]? backup = null;
        await using var restored = new InspectionHost(configuration: restoredStorage.Configuration, activeIdentity: identity, prepareDatabase: async connection =>
        {
            backup = await infrastructure.RestoreBackupAsync(source.ConnectionString, connection);
        });
        await restored.StartAsync();
        using var reader = await restored.CreateClientAsync("administrator");
        using var login = await reader.GetAsync(accessPath);
        await EnsureSuccessAsync(login);
        var access = await login.Content.ReadFromJsonAsync<JsonElement>(Json);
        var subject = access.GetProperty("authSubject").GetString();
        Assert.Equal(originalSubject, subject);
        var link = await PostAsync<AttachmentDownloadDto>(reader, $"/api/attachments/{upload.AttachmentId}/download", new { });
        using var storageClient = new HttpClient();
        using var download = await storageClient.GetAsync(link.Url);
        download.EnsureSuccessStatusCode();
        var downloaded = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, downloaded);
        using var response = await reader.GetAsync(path);
        await EnsureSuccessAsync(response);
        var after = (await response.Content.ReadFromJsonAsync<DocumentDto>(Json))!;
        var restoredBalance = await ReadBalanceAsync(restored, register, month);
        Assert.Equal(originalBalance, restoredBalance);
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
            restoredLogin = new { status = (int)login.StatusCode, subject, originalSubject },
            restoredAttachment = new
            {
                status = (int)download.StatusCode,
                id = upload.AttachmentId,
                bytes = downloaded.Length,
                originalSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                sha256 = Convert.ToHexStringLower(SHA256.HashData(downloaded))
            },
            beforeBackup = new { before.Id, before.Number, before.Status, before.Payload, balance = originalBalance },
            afterRestore = new { after.Id, after.Number, after.Status, after.Payload, balance = restoredBalance }
        }));
    }

    private static async Task<decimal> ReadBalanceAsync(InspectionHost host, Guid register, DateOnly month)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
        var page = await reads.GetBalancesPageAsync(new OperationalRegisterMonthlyProjectionPageRequest(register, month, month.AddMonths(1)));
        return Assert.Single(page.Lines).Values["hours"];
    }
}
