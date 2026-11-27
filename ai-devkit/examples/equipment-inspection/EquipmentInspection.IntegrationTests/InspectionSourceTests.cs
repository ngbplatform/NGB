using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Runtime.Documents;
using Xunit;
using Xunit.Abstractions;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class InspectionSourceTests(ITestOutputHelper output)
{
    private const string Documents = "/api/documents/example.inspection";

    [Fact]
    public async Task Reinspection_requires_a_confirmed_source_effective_on_its_own_date()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Date control")));
        var original = await CreateAsync(admin, equipment.Id);
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var effective = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc);
        await drafts.UpdateDraftAsync(original.Id, number: null, dateUtc: effective);
        var source = await ConfirmAsync(admin, original);
        var derived = await DeriveAsync(admin, source, equipment.Id);
        var path = Documents + "/" + derived.Id;
        await drafts.UpdateDraftAsync(derived.Id, number: null, dateUtc: effective.AddDays(-1));
        var before = (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!;
        using var refused = await ExecutePostAsync(admin, path, await VersionAsync(admin, derived.Id), "before-source");
        const string expectedReason = "example.inspection.source_unavailable";
        var reason = await AssertErrorAsync(refused, HttpStatusCode.Conflict, expectedReason);
        var after = (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!;
        Assert.Equal(before.Status, after.Status);
        await AssertNoEffectsAsync(admin, path);

        await drafts.UpdateDraftAsync(derived.Id, number: null, dateUtc: effective);
        using var control = await ExecutePostAsync(admin, path, await VersionAsync(admin, derived.Id), "at-source");
        await EnsureSuccessAsync(control);
        var posted = (await admin.GetFromJsonAsync<DocumentDto>(path, Json))!;
        Assert.Equal(DocumentStatus.Posted, posted.Status);
        output.WriteLine("NGB-CHECK " + JsonSerializer.Serialize(new
        {
            kind = "effective-prerequisite", target = "document:" + EquipmentDefinitions.InspectionCode, key = "source-confirmation",
            attemptAt = effective.AddDays(-1), prerequisiteAt = effective, controlAt = effective,
            persistedControlId = posted.Id, controlStatus = (int)control.StatusCode, refusalStatus = (int)refused.StatusCode,
            expectedReason, reason,
            beforeRefusal = new { before.Id, before.Status }, afterRefusal = new { after.Id, after.Status }
        }));
    }

    [Fact]
    public async Task Search_filters_before_paging_and_preview_rechecks_status_scope_and_permission()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        using var anonymous = host.CreateAnonymousClient();
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Source search")));
        var draft = await CreateAsync(admin, equipment.Id);
        var first = await ConfirmAsync(admin, await CreateAsync(admin, equipment.Id));
        var second = await ConfirmAsync(admin, await CreateAsync(admin, equipment.Id));
        var search = $"/api/inspection-sources?equipmentId={equipment.Id}";
        var all = (await admin.GetFromJsonAsync<InspectionSource[]>(search, Json))!;
        Assert.Equal(2, all.Length);
        Assert.DoesNotContain(all, item => item.Id == draft.Id);
        Assert.Contains(all, item => item.Id == first.Id);
        Assert.Contains(all, item => item.Id == second.Id);
        Assert.Equal(all.Take(1), await admin.GetFromJsonAsync<InspectionSource[]>(search + "&limit=1", Json));
        Assert.Equal(all.Skip(1), await admin.GetFromJsonAsync<InspectionSource[]>(search + "&offset=1&limit=1", Json));
        Assert.Empty((await admin.GetFromJsonAsync<InspectionSource[]>(search + "&query=missing", Json))!);
        Assert.Single((await admin.GetFromJsonAsync<InspectionSource[]>(search + "&query=" + first.Number, Json))!);
        Assert.Equal(first.Id, (await admin.GetFromJsonAsync<InspectionSource>(
            $"/api/inspection-sources/{first.Id}?equipmentId={equipment.Id}", Json))!.Id);

        foreach (var path in new[] { search + "&limit=101", search + "&offset=-1", "/api/inspection-sources" })
        {
            using var invalid = await admin.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        foreach (var id in new[] { draft.Id, Guid.NewGuid() })
        {
            using var unavailable = await admin.GetAsync($"/api/inspection-sources/{id}?equipmentId={equipment.Id}");
            await AssertErrorAsync(unavailable, HttpStatusCode.Conflict, "example.inspection.source_unavailable");
        }
        using var wrongEquipment = await admin.GetAsync($"/api/inspection-sources/{first.Id}?equipmentId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Conflict, wrongEquipment.StatusCode);
        foreach (var path in new[] { search, $"/api/inspection-sources/{first.Id}?equipmentId={equipment.Id}" })
        {
            using var forbidden = await reader.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var denied = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
    }

    [Fact]
    public async Task Source_lookup_reads_a_larger_page_with_the_same_number_of_database_commands()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        const string actor = "administrator";
        const string operation = "available-sources";
        using var admin = await host.CreateClientAsync(actor);
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Lookup volume")));
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var firstDay = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var seeded = new List<InspectionSource>();
        for (var index = 0; index < 12; index++)
        {
            var result = index % 2 == 0 ? "Passed" : "Rejected";
            var draft = await PostAsync<DocumentDto>(admin, Documents, CompleteInspectionPayload(equipment.Id, result));
            await drafts.UpdateDraftAsync(draft.Id, number: null, dateUtc: firstDay.AddDays(index));
            var confirmed = await ConfirmAsync(admin, draft);
            Assert.False(string.IsNullOrWhiteSpace(confirmed.Number));
            seeded.Add(new InspectionSource(confirmed.Id, confirmed.Number, equipment.Id, result));
        }

        // Each date is unique. The lookup presents the most recent source first.
        seeded.Reverse();

        var path = $"/api/inspection-sources?equipmentId={equipment.Id}&limit=";
        var warm = (await admin.GetFromJsonAsync<InspectionSource[]>(path + seeded.Count, Json))!;
        Assert.Equal(seeded.ToArray(), warm);

        async Task<PageEvidence<InspectionSource>> ReadPageAsync(int limit)
        {
            var measured = await MeasureRequestAsync(admin, operation, path + limit);
            using var response = measured.Response;
            await EnsureSuccessAsync(response);
            var page = (await response.Content.ReadFromJsonAsync<InspectionSource[]>(Json))!;
            var expected = seeded.Take(limit).ToArray();
            Assert.Equal(expected, page);
            Assert.True(measured.Queries > 0);
            var request = response.RequestMessage!;
            var requestOperation = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            return new PageEvidence<InspectionSource>(actor, operation, requestOperation, (int)response.StatusCode, measured.Queries, expected, page);
        }

        var small = await ReadPageAsync(1);
        var large = await ReadPageAsync(12);
        Assert.True(large.Queries <= small.Queries);
        output.WriteLine("NGB-CHECK " + JsonSerializer.Serialize(new
        {
            kind = "query-growth", target = "document:" + EquipmentDefinitions.InspectionCode, key = operation,
            operation = small.RequestOperation,
            valueFields = new[] { "number", "equipmentId", "result" },
            small,
            large
        }, Json));
    }

    [Fact]
    public async Task The_price_reader_returns_the_expected_service_rates_with_bounded_query_growth()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        const string actor = "administrator";
        const string type = EquipmentServiceDefinitions.DocumentCode;
        const string operation = "dated-service-prices";
        const string endpoint = "/api/documents/" + type;
        const string priceList = "Service rate fixture 2026-04-09";
        using var client = await host.CreateClientAsync(actor);
        var expected = new List<ServicePrice>();
        for (var index = 0; index < 12; index++)
        {
            var amount = 12.75m + index;
            var draft = await PostAsync<DocumentDto>(client, endpoint,
                Payload(("display", $"{priceList} {index:D2}"), ("amount", amount)));
            expected.Add(new ServicePrice(draft.Id, amount));
        }

        var path = endpoint + "?search=" + Uri.EscapeDataString(priceList) + "&limit=";
        using var warm = await client.GetAsync(path + expected.Count);
        await EnsureSuccessAsync(warm);

        async Task<PageEvidence<ServicePrice>> ReadPricesAsync(int limit)
        {
            var measured = await MeasureRequestAsync(client, operation, path + limit);
            using var response = measured.Response;
            await EnsureSuccessAsync(response);
            var page = (await response.Content.ReadFromJsonAsync<PageResponseDto<DocumentDto>>(Json))!;
            var prices = page.Items.Select(item => new ServicePrice(item.Id, item.Payload.Fields!["amount"].GetDecimal())).ToArray();
            var expectedPrices = expected.Take(limit).ToArray();
            Assert.Equal(expectedPrices, prices);
            Assert.True(measured.Queries > 0);
            var request = response.RequestMessage!;
            return new PageEvidence<ServicePrice>(actor, operation, $"{request.Method} {request.RequestUri!.AbsolutePath}",
                (int)response.StatusCode, measured.Queries, expectedPrices, prices);
        }

        var small = await ReadPricesAsync(1);
        var large = await ReadPricesAsync(12);
        Assert.True(large.Queries <= small.Queries);
        output.WriteLine("NGB-CHECK " + JsonSerializer.Serialize(new
        {
            kind = "query-growth", target = "document:" + type, key = operation, operation = small.RequestOperation,
            valueFields = new[] { "amount" }, small, large
        }, Json));
    }

    private static async Task<(HttpResponseMessage Response, int Queries)> MeasureRequestAsync(
        HttpClient client, string operation, string path)
    {
        using var trace = new Activity(operation).SetIdFormat(ActivityIdFormat.W3C).Start();
        var queries = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Npgsql", StringComparison.Ordinal)
                || source.Name is "System.Net.Http" or "Microsoft.AspNetCore",
            Sample = static (ref _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == trace.TraceId
                    && (activity.GetTagItem("db.query.text") is not null || activity.GetTagItem("db.statement") is not null))
                    Interlocked.Increment(ref queries);
            }
        };
        ActivitySource.AddActivityListener(listener);
        var response = await client.GetAsync(path);
        return (response, queries);
    }

    private sealed record ServicePrice(Guid Id, decimal Amount);

    private sealed record PageEvidence<T>(string Actor, string Operation, string RequestOperation, int Status, int Queries,
        T[] ExpectedRows, T[] ActualRows)
    {
        public int ReturnedRows => ActualRows.Length;
    }

    [Fact]
    public async Task Competing_confirmations_consume_one_source_and_unpost_restores_eligibility()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Reinspection race")));
        var source = await ConfirmAsync(admin, await CreateAsync(admin, equipment.Id));
        var first = await DeriveAsync(admin, source, equipment.Id);
        var second = await DeriveAsync(admin, source, equipment.Id);
        var firstVersion = await VersionAsync(admin, first.Id);
        var secondVersion = await VersionAsync(admin, second.Id);
        var responses = await Task.WhenAll(
            ExecutePostAsync(admin, Documents + "/" + first.Id, firstVersion, "first-reinspection"),
            ExecutePostAsync(admin, Documents + "/" + second.Id, secondVersion, "second-reinspection"));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var winner = await ReadActionAsync(Assert.Single(responses, item => item.IsSuccessStatusCode));
        const string expectedReason = "example.inspection.source_unavailable";
        var reason = await AssertErrorAsync(Assert.Single(responses, item => !item.IsSuccessStatusCode),
            HttpStatusCode.Conflict, expectedReason);
        await using var scope = host.Services.CreateAsyncScope();
        var relationships = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipService>();
        var attempts = new List<object>();
        foreach (var (draft, response) in new[] { (first, firstResponse), (second, secondResponse) })
        {
            var reopened = (await admin.GetFromJsonAsync<DocumentDto>(Documents + "/" + draft.Id, Json))!;
            Assert.Equal(response.IsSuccessStatusCode ? DocumentStatus.Posted : DocumentStatus.Draft, reopened.Status);
            var link = Assert.Single(await relationships.ListOutgoingAsync(draft.Id),
                item => item.RelationshipCode == InspectionDerivationDefinitions.RelationshipCode);
            Assert.Equal(source.Id, link.ToDocumentId);
            attempts.Add(new
            {
                draft.Id, reopenedId = reopened.Id, sourceId = link.ToDocumentId, state = reopened.Status.ToString(),
                status = (int)response.StatusCode, reason = response.IsSuccessStatusCode ? null : reason
            });
        }
        output.WriteLine("NGB-CHECK " + JsonSerializer.Serialize(new
        {
            kind = "competing-consumers", target = "document:" + EquipmentDefinitions.InspectionCode,
            key = "reinspection", sourceId = source.Id, expectedReason, attempts
        }, Json));
        var lookup = $"/api/inspection-sources?equipmentId={equipment.Id}";
        Assert.DoesNotContain((await admin.GetFromJsonAsync<InspectionSource[]>(lookup, Json))!, item => item.Id == source.Id);
        using var stalePreview = await admin.GetAsync($"/api/inspection-sources/{source.Id}?equipmentId={equipment.Id}");
        Assert.Equal(HttpStatusCode.Conflict, stalePreview.StatusCode);
        var winnerPath = Documents + "/" + winner.Document.Id;
        using var repost = await ExecuteActionAsync(admin, winnerPath, "repost", winner.DocumentVersion, "own-source-repost");
        var reposted = await ReadActionAsync(repost);
        using var unpost = await ExecuteActionAsync(admin, winnerPath, "unpost", reposted.DocumentVersion, "release-source");
        await ReadActionAsync(unpost);
        Assert.Contains((await admin.GetFromJsonAsync<InspectionSource[]>(lookup, Json))!, item => item.Id == source.Id);
    }

    private static async Task<long> VersionAsync(HttpClient client, Guid id)
        => (await client.GetFromJsonAsync<DocumentEditorStateDto>(Documents + "/" + id + "/editor-state", Json))!.DocumentVersion;

    private static Task<DocumentDto> CreateAsync(HttpClient client, Guid equipmentId)
        => InspectionRequests.PostAsync<DocumentDto>(client, Documents, CompleteInspectionPayload(equipmentId));

    private static async Task<DocumentDto> ConfirmAsync(HttpClient client, DocumentDto draft)
    {
        using var response = await ExecutePostAsync(client, Documents + "/" + draft.Id, await VersionAsync(client, draft.Id), Guid.NewGuid().ToString());
        return (await ReadActionAsync(response)).Document;
    }

    private static async Task<DocumentDto> DeriveAsync(HttpClient client, DocumentDto source, Guid equipmentId)
    {
        using var response = await ExecuteActionAsync(client, Documents + "/" + source.Id,
            InspectionDerivationDefinitions.ActionCode, await VersionAsync(client, source.Id), Guid.NewGuid().ToString());
        var draft = (await ReadActionAsync(response)).CreatedDocument!;
        using var save = await client.PutAsJsonAsync(Documents + "/" + draft.Id, CompleteInspectionPayload(equipmentId));
        await EnsureSuccessAsync(save);
        return (await save.Content.ReadFromJsonAsync<DocumentDto>(Json))!;
    }
}
