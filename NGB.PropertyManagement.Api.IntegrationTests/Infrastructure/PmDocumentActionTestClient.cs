using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NGB.Contracts.Documents;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

internal static class PmDocumentActionTestClient
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    public static async Task<DocumentActionDto> GetDocumentActionAsync(
        this HttpClient client,
        string documentType,
        Guid documentId,
        string actionCode,
        CancellationToken ct = default)
    {
        var path = $"/api/documents/{Uri.EscapeDataString(documentType)}/{documentId:D}/editor-state";
        var state = await client.GetFromJsonAsync<DocumentEditorStateDto>(path, Json, ct) 
                    ?? throw new InvalidOperationException("The document editor-state response was empty.");
        return state.Actions.Single(x => string.Equals(x.Code, actionCode, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
