using System.Net;
using System.Net.Http.Json;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionDeletionTests
{
    [Fact]
    public async Task Draft_deletion_requires_permission_and_posted_documents_are_preserved()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var (path, version) = await CreateCompleteInspectionAsync(admin);

        using var denied = await reader.DeleteAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var unchanged = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        Assert.Equal(version, unchanged!.DocumentVersion);
        Assert.Equal(DocumentStatus.Draft, unchanged.Document.Status);

        using var deleted = await admin.DeleteAsync(path);
        await EnsureSuccessAsync(deleted);
        using var absent = await admin.GetAsync(path);
        await AssertErrorAsync(absent, HttpStatusCode.NotFound, "doc.not_found");
        using var repeatedDelete = await admin.DeleteAsync(path);
        await EnsureSuccessAsync(repeatedDelete);

        var (postedPath, postedVersion) = await CreateCompleteInspectionAsync(admin);
        using var postResponse = await ExecutePostAsync(admin, postedPath, postedVersion, "deletion-guard");
        var posted = await ReadActionAsync(postResponse);
        using var cannotDelete = await admin.DeleteAsync(postedPath);
        await AssertErrorAsync(cannotDelete, HttpStatusCode.Conflict, "doc.workflow.state_mismatch");
        var preserved = await admin.GetFromJsonAsync<DocumentEditorStateDto>(postedPath + "/editor-state", Json);
        Assert.Equal(posted.DocumentVersion, preserved!.DocumentVersion);
        Assert.Equal(DocumentStatus.Posted, preserved.Document.Status);
        Assert.Equal("Passed", preserved.Document.Payload.Fields!["result"].GetString());
        await AssertNoEffectsAsync(admin, postedPath);
    }
}
