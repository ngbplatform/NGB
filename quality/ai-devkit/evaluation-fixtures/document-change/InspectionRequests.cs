using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Effects;
using NGB.Contracts.Services;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

internal static class InspectionRequests
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    internal static RecordPayload Payload(params (string Name, object Value)[] fields)
    {
        return new RecordPayload(fields.ToDictionary(field => field.Name,
            field => JsonSerializer.SerializeToElement(field.Value)));
    }

    internal static async Task<T> PostAsync<T>(HttpClient client, string path, object payload)
    {
        using var response = await client.PostAsJsonAsync(path, payload);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    internal static Task<HttpResponseMessage> ExecutePostAsync(HttpClient client, string path, long version, string key)
        => ExecuteActionAsync(client, path, "post", version, key);

    internal static async Task<HttpResponseMessage> ExecuteActionAsync(
        HttpClient client, string path, string action, long version, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "/actions/" + action)
        {
            Content = JsonContent.Create(new ExecuteDocumentActionRequestDto(version))
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    internal static async Task<ExecuteDocumentActionResultDto> ReadActionAsync(HttpResponseMessage response)
    {
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<ExecuteDocumentActionResultDto>(Json))!;
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode,
            $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    internal static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, error.GetProperty("error").GetProperty("code").GetString());
    }

    internal static async Task AssertNoEffectsAsync(HttpClient client, string path)
    {
        var effects = await client.GetFromJsonAsync<DocumentEffectsDto>(path + "/effects", Json);
        Assert.Empty(effects!.AccountingEntries);
        Assert.Empty(effects.OperationalRegisterMovements);
        Assert.Empty(effects.ReferenceRegisterWrites);
    }

    internal static async Task<(string Path, long Version)> CreateCompleteInspectionAsync(HttpClient client)
    {
        var equipment = await PostAsync<CatalogItemDto>(client, "/api/catalogs/example.equipment",
            Payload(("display", "Lifecycle inspection unit")));
        var draft = await PostAsync<DocumentDto>(client, "/api/documents/example.inspection",
            Payload(("equipment_id", equipment.Id), ("result", "Passed")));
        var path = $"/api/documents/example.inspection/{draft.Id}";
        var state = await client.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        return (path, state!.DocumentVersion);
    }
}
