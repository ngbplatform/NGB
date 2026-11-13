using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Effects;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Independent_packages_distinguish_no_effect_posting_from_missing_handler(bool registerWorkflowHandler)
    {
        await using var host = new InspectionHost(registerWorkflowHandler);
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Portable inspection unit")));
        var savedEquipment = await admin.GetFromJsonAsync<CatalogItemDto>($"/api/catalogs/example.equipment/{equipment.Id}", Json);
        Assert.Equal("Portable inspection unit", savedEquipment!.Display);
        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection", new RecordPayload());
        Assert.Equal(DocumentStatus.Draft, draft.Status);
        var path = $"/api/documents/example.inspection/{draft.Id}";
        var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);

        using (var invalid = await ExecutePostAsync(admin, path, state!.DocumentVersion, "incomplete-inspection"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using (var update = await admin.PutAsJsonAsync(path, CompleteInspectionPayload(equipment.Id)))
        {
            await EnsureSuccessAsync(update);
        }
        state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        var updated = await admin.GetFromJsonAsync<DocumentDto>(path, Json);
        Assert.Equal(DocumentStatus.Draft, updated!.Status);
        var equipmentReference = updated.Payload.Fields!["equipment_id"].Deserialize<RefValueDto>(Json);
        Assert.Equal(equipment.Id, equipmentReference!.Id);
        Assert.Equal("Passed", updated.Payload.Fields["result"].GetString());

        using var reader = await host.CreateClientAsync("reader");
        using var forbiddenCreate = await reader.PostAsJsonAsync("/api/catalogs/example.equipment",
            Payload(("display", "Unauthorized equipment")));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);
        using var forbiddenRead = await reader.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
        using var forbiddenPost = await ExecutePostAsync(reader, path, state!.DocumentVersion, "unauthorized-inspection");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenPost.StatusCode);

        using var posted = await ExecutePostAsync(admin, path, state!.DocumentVersion, "complete-inspection");
        if (registerWorkflowHandler)
        {
            await EnsureSuccessAsync(posted);
        }
        else
        {
            Assert.Equal(HttpStatusCode.InternalServerError, posted.StatusCode);
            var error = await posted.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("doc.posting.handler.not_configured", error.GetProperty("error").GetProperty("code").GetString());
        }
        var completed = await admin.GetFromJsonAsync<DocumentDto>(path, Json);
        Assert.Equal(registerWorkflowHandler ? DocumentStatus.Posted : DocumentStatus.Draft, completed!.Status);

        var effects = await admin.GetFromJsonAsync<DocumentEffectsDto>(path + "/effects", Json);
        Assert.Empty(effects!.AccountingEntries);
        Assert.Empty(effects.OperationalRegisterMovements);
        Assert.Empty(effects.ReferenceRegisterWrites);

        using var anonymous = host.CreateAnonymousClient();
        using var unauthorized = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }
}
