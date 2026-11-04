using System.Net;
using System.Net.Http.Json;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class EquipmentCatalogTests
{
    private const string CatalogPath = "/api/catalogs/example.equipment";

    [Fact]
    public async Task Point_batch_reads_and_updates_preserve_unrelated_equipment()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var first = await PostAsync<CatalogItemDto>(admin, CatalogPath, Payload(("display", "Inspection kit A")));
        var second = await PostAsync<CatalogItemDto>(admin, CatalogPath, Payload(("display", "Inspection kit B")));
        var path = $"{CatalogPath}/{first.Id}";

        using var update = await admin.PutAsJsonAsync(path, Payload(("display", "Calibrated kit A")));
        await EnsureSuccessAsync(update);
        var saved = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.Equal(first.Id, saved!.Id);
        Assert.Equal("Calibrated kit A", saved.Display);
        Assert.Equal("Calibrated kit A", saved.Payload.Fields!["display"].GetString());
        var unchanged = await admin.GetFromJsonAsync<CatalogItemDto>($"{CatalogPath}/{second.Id}", Json);
        Assert.Equal("Inspection kit B", unchanged!.Display);

        var batch = await PostAsync<LookupItemDto[]>(admin, CatalogPath + "/by-ids",
            new { ids = new[] { first.Id, second.Id } });
        Assert.Equal(new[] { first.Id, second.Id }.Order(), batch.Select(item => item.Id).Order());
        Assert.Equal("Calibrated kit A", Assert.Single(batch, item => item.Id == first.Id).Label);

        using var deniedRead = await reader.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        using var deniedBatch = await reader.PostAsJsonAsync(CatalogPath + "/by-ids", new { ids = new[] { first.Id } });
        Assert.Equal(HttpStatusCode.Forbidden, deniedBatch.StatusCode);
        using var deniedUpdate = await reader.PutAsJsonAsync(path, Payload(("display", "Unauthorized change")));
        Assert.Equal(HttpStatusCode.Forbidden, deniedUpdate.StatusCode);
        saved = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.Equal("Calibrated kit A", saved!.Display);
    }

    [Fact]
    public async Task Filtered_pages_are_bounded_stable_and_do_not_duplicate_records()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var expected = new List<Guid>();
        foreach (var label in new[] { "Meter Alpha", "Meter Bravo", "Meter Charlie" })
        {
            var item = await PostAsync<CatalogItemDto>(admin, CatalogPath, Payload(("display", label)));
            expected.Add(item.Id);
        }
        await PostAsync<CatalogItemDto>(admin, CatalogPath, Payload(("display", "Unrelated equipment")));

        var pagePath = CatalogPath + "?search=Meter&limit=2&includeTotal=true";
        var first = await admin.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(pagePath, Json);
        var second = await admin.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(pagePath + "&offset=2", Json);
        var repeated = await admin.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(pagePath, Json);
        Assert.Equal(2, first!.Items.Count);
        Assert.Single(second!.Items);
        Assert.Equal(3, first.Total);
        Assert.Equal(3, second.Total);
        Assert.Equal(2, first.Limit);
        Assert.Equal(2, second.Offset);
        Assert.Equal(first.Items.Select(item => item.Id), repeated!.Items.Select(item => item.Id));
        Assert.Equal(expected.Order(), first.Items.Concat(second.Items).Select(item => item.Id).Order());

        var empty = await admin.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(pagePath + "&offset=4", Json);
        Assert.Empty(empty!.Items);
        using var denied = await reader.GetAsync(pagePath);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Deletion_marks_require_permission_and_are_reversible()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var item = await PostAsync<CatalogItemDto>(admin, CatalogPath, Payload(("display", "Retired instrument")));
        var path = $"{CatalogPath}/{item.Id}";

        using var deniedMark = await reader.PostAsync(path + "/mark-for-deletion", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedMark.StatusCode);
        var unchanged = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.False(unchanged!.IsMarkedForDeletion);
        using var mark = await admin.PostAsync(path + "/mark-for-deletion", null);
        await EnsureSuccessAsync(mark);
        var marked = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.True(marked!.IsMarkedForDeletion);

        using var deniedUnmark = await reader.PostAsync(path + "/unmark-for-deletion", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedUnmark.StatusCode);
        marked = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.True(marked!.IsMarkedForDeletion);
        using var unmark = await admin.PostAsync(path + "/unmark-for-deletion", null);
        await EnsureSuccessAsync(unmark);
        var restored = await admin.GetFromJsonAsync<CatalogItemDto>(path, Json);
        Assert.False(restored!.IsMarkedForDeletion);
        Assert.Equal(item.Id, restored.Id);
        Assert.Equal(item.Display, restored.Display);
    }
}
