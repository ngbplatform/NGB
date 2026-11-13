using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using Npgsql;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class TypedDocumentGuardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Posted_document_storage_rejects_direct_deletion_and_rolls_back_head_and_parts(bool checklist)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var typeCode = checklist ? ChecklistDefinitions.DocumentCode : EquipmentDefinitions.InspectionCode;
        object payload;
        if (checklist)
        {
            payload = new
            {
                fields = new { display = "Guarded checklist", reviewed = true },
                parts = new
                {
                    checks = new
                    {
                        rows = new[] { new { line_no = 1, description = "Guard present", passed = true } }
                    }
                }
            };
        }
        else
        {
            var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
                Payload(("display", "Guarded inspection equipment")));
            payload = CompleteInspectionPayload(equipment.Id);
        }

        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/" + typeCode, payload);
        var path = "/api/documents/" + typeCode + "/" + draft.Id;
        var state = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var response = await ExecutePostAsync(admin, path, state.DocumentVersion, "guard-post");
        var postedAction = await ReadActionAsync(response);
        var posted = (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!;
        Assert.Equal(DocumentStatus.Posted, posted.Status);
        var before = JsonSerializer.Serialize(posted, Json);
        var audit = await admin.GetStringAsync($"/api/audit/entities/Document/{draft.Id}");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var storage = scope.ServiceProvider.GetServices<IDocumentTypeStorage>().Single(item => item.TypeCode == typeCode);
            var error = await Assert.ThrowsAsync<PostgresException>(() => unitOfWork.ExecuteInUowTransactionAsync(
                ct => storage.DeleteDraftAsync(draft.Id, ct)));
            Assert.Equal("55000", error.SqlState);
        }

        var after = (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!;
        Assert.Equal(before, JsonSerializer.Serialize(after, Json));
        Assert.Equal(audit, await admin.GetStringAsync($"/api/audit/entities/Document/{draft.Id}"));
        if (checklist)
            Assert.Single(after.Payload.Parts![ChecklistDefinitions.ChecksPartCode].Rows);

        using var unpost = await ExecuteActionAsync(admin, path, "unpost", postedAction.DocumentVersion, "guard-unpost");
        await ReadActionAsync(unpost);
        using var update = await admin.PutAsJsonAsync(path, Payload(("display", "Editable draft after Unpost")));
        await EnsureSuccessAsync(update);
        Assert.Equal(DocumentStatus.Draft, (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!.Status);
    }
}
