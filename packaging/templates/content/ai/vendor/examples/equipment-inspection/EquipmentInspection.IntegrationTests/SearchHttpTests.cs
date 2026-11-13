using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Contracts.Reporting;
using NGB.Contracts.Security;
using NGB.Contracts.Services;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class SearchHttpTests
{
    private const string Path = "/api/equipment-search";

    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Both_administrators_search_installed_definitions_with_canonical_filters(string username)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode, "Search owners");
        }

        using var admin = await host.CreateClientAsync(username);
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Inspection")));
        var document = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
            Payload(("display", "Inspection search unit"), ("equipment_id", equipment.Id), ("result", "Passed")));
        var catalog = await SearchAsync(admin, "Inspection", "&kind=Catalog&typeCode=example.equipment");
        Assert.Equal(equipment.Id, Assert.Single(catalog.Items).Id);
        Assert.False(catalog.Truncated);
        Assert.Empty(catalog.UnavailableSources);
        var documents = await SearchAsync(admin, "Inspection", "&kind=Document&typeCode=example.inspection");
        var documentHit = Assert.Single(documents.Items);
        Assert.Equal(document.Id, documentHit.Id);
        Assert.Equal("document", documentHit.Kind);
        Assert.False(documents.Truncated);
        Assert.Empty(documents.UnavailableSources);
        var documentScope = await SearchAsync(admin, "Inspection", "&kind=Document");
        Assert.Equal(document.Id, Assert.Single(documentScope.Items).Id);
        var reports = await SearchAsync(admin, "Inspection", "&kind=Report&typeCode=example.inspection-summary");
        var reportHit = Assert.Single(reports.Items);
        Assert.Equal("example.inspection-summary", reportHit.TypeCode);
        Assert.Equal("report", reportHit.Kind);
        Assert.Null(reportHit.Id);
        foreach (var typeCode in new[] { "example.unknown", "EXAMPLE.EQUIPMENT" })
        {
            Assert.Empty((await SearchAsync(admin, "Inspection", "&kind=Catalog&typeCode=" + typeCode)).Items);
        }
    }

    [Fact]
    public async Task Search_bounds_results_and_omits_deleted_catalog_records()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var ids = new List<Guid>();
        for (var index = 0; index < 7; index++)
        {
            var item = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
                Payload(("display", $"Bounded search {index}")));
            ids.Add(item.Id);
        }

        var bounded = await SearchAsync(admin, "Bounded search", "&kind=Catalog&limit=2");
        Assert.Equal(2, bounded.Items.Count);
        Assert.True(bounded.Truncated);
        Assert.Empty(bounded.UnavailableSources);
        var perType = await SearchAsync(admin, "Bounded search", "&kind=Catalog&limit=50");
        Assert.Equal(6, perType.Items.Count);
        Assert.True(perType.Truncated);
        Assert.All(perType.Items, item => Assert.Contains(item.Id!.Value, ids));
        foreach (var id in ids.Take(2))
        {
            using var deleted = await admin.PostAsync($"/api/catalogs/example.equipment/{id}/mark-for-deletion", null);
            await EnsureSuccessAsync(deleted);
        }

        var remaining = await SearchAsync(admin, "Bounded search", "&kind=Catalog&limit=50");
        Assert.Equal(5, remaining.Items.Count);
        Assert.False(remaining.Truncated);
        Assert.Equal(ids.Skip(2).Order(), remaining.Items.Select(item => item.Id!.Value).Order());
    }

    [Fact]
    public async Task Invalid_search_requests_are_rejected_at_the_public_boundary()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        foreach (var suffix in new[]
        {
            "?query=%20", "?query=" + new string('a', 257), "?query=Inspection&limit=0",
            "?query=Inspection&limit=51", "?query=Inspection&kind=99", "?query=Inspection&kind=unknown",
            "?query=Inspection&typeCode=" + new string('a', 129)
        })
        {
            using var response = await admin.GetAsync(Path + suffix);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
                $"Expected HTTP 400 for {suffix}; received {(int)response.StatusCode}.");
        }

        Assert.Empty((await SearchAsync(admin, new string('a', 256), "&limit=50")).Items);
        var withoutType = await SearchAsync(admin, "Inspection", "&kind=Report");
        var blankType = await SearchAsync(admin, "Inspection", "&kind=Report&typeCode=%20");
        Assert.Contains(withoutType.Items, item => item.TypeCode == InspectionReports.Summary);
        Assert.Contains(withoutType.Items, item => item.TypeCode == InspectionReports.OutcomeRate);
        Assert.Equal(withoutType.Items.ToArray(), blankType.Items.ToArray());
        Assert.All(blankType.Items, item => Assert.Equal("report", item.Kind));
    }

    [Fact]
    public async Task Current_search_permissions_follow_role_changes_and_block_inactive_accounts()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("reader", true, "inspection.search-reader", "Search reader");
        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode, "Administrator");
        await host.LinkUserAsync("imitator", true, "inspection.search-observer", "Administrator");
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Inspection search access")));
        using var reader = await host.CreateClientAsync("reader");
        Assert.Empty((await SearchAsync(reader, "Inspection")).Items);
        var access = (await reader.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access"))!;
        var role = await PostAsync<RoleDetailsDto>(admin, "/api/security/roles",
            new CreateRoleRequestDto("inspection.equipment-search", "Equipment search", null,
                [new PermissionAssignmentDto("catalog", "example.equipment", "view")]));
        using var assign = await admin.PutAsJsonAsync($"/api/security/users/{access.UserId}/roles",
            new ReplaceUserRolesRequestDto([role.RoleId]));
        await EnsureSuccessAsync(assign);
        Assert.Empty((await SearchAsync(reader, "Inspection")).Items);
        using var lookup = await admin.PutAsJsonAsync($"/api/security/roles/{role.RoleId}/permissions",
            new ReplaceRolePermissionsRequestDto([
                new PermissionAssignmentDto("catalog", "example.equipment", "view"),
                new PermissionAssignmentDto("catalog", "example.equipment", "lookup")
            ]));
        await EnsureSuccessAsync(lookup);
        Assert.Equal(equipment.Id, Assert.Single((await SearchAsync(reader, "Inspection")).Items).Id);
        using var revoke = await admin.PutAsJsonAsync($"/api/security/roles/{role.RoleId}/permissions",
            new ReplaceRolePermissionsRequestDto([]));
        await EnsureSuccessAsync(revoke);
        Assert.Empty((await SearchAsync(reader, "Inspection")).Items);
        foreach (var username in new[] { "inactive-administrator", "imitator" })
        {
            using var denied = await host.CreateClientAsync(username);
            Assert.Empty((await SearchAsync(denied, "Inspection")).Items);
        }
        using var anonymous = host.CreateAnonymousClient();
        using var response = await anonymous.GetAsync(Path + "?query=Inspection");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_failed_search_source_is_explicit_and_preserves_other_authorized_results()
    {
        await using var host = new InspectionHost(configureServices: services =>
            services.Replace(ServiceDescriptor.Scoped<IAuthorizedReportReadService, UnavailableReports>()));
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Inspection available equipment")));
        var result = await SearchAsync(admin, "Inspection");
        Assert.Equal(equipment.Id, Assert.Single(result.Items).Id);
        Assert.True(result.Truncated);
        Assert.Equal(new[] { "reports" }, result.UnavailableSources);
        var catalog = await SearchAsync(admin, "Inspection", "&kind=Catalog");
        Assert.Equal(equipment.Id, Assert.Single(catalog.Items).Id);
        Assert.False(catalog.Truncated);
        Assert.Empty(catalog.UnavailableSources);
    }

    [Fact]
    public async Task General_journal_search_uses_specialized_reads_without_disabling_other_documents()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var journal = await PostAsync<GeneralJournalEntryDetailsDto>(admin, "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc)));
        var inspection = await PostAsync<DocumentDto>(admin, "/api/documents/example.inspection",
            Payload(("display", "Inspection alongside journal")));
        var scoped = await SearchAsync(admin, journal.Document.Number!, "&kind=GeneralJournalEntry");
        var hit = Assert.Single(scoped.Items);
        Assert.Equal(journal.Document.Id, hit.Id);
        Assert.Equal("generalJournalEntry", hit.Kind);
        Assert.Equal(journal.Document.Number, hit.Title);
        Assert.False(scoped.Truncated);
        Assert.Empty(scoped.UnavailableSources);
        var unscoped = await SearchAsync(admin, journal.Document.Number!);
        Assert.Contains(unscoped.Items, item => item.Id == journal.Document.Id);
        Assert.Empty(unscoped.UnavailableSources);
        var all = await SearchAsync(admin, "Inspection");
        Assert.False(all.Truncated);
        Assert.Empty(all.UnavailableSources);
        Assert.Contains(all.Items, item => item.Kind == "report");
        Assert.Contains(all.Items, item => item.Id == inspection.Id && item.Kind == "document");

        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var error = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() =>
            documents.LookupAcrossTypesAsync([AccountingDocumentTypeCodes.GeneralJournalEntry],
                journal.Document.Number, 6, false, CancellationToken.None));
        Assert.Contains("must define a display column", error.Message);
        Assert.Contains(AccountingDocumentTypeCodes.GeneralJournalEntry, error.Message);
        var typed = (await admin.GetFromJsonAsync<GeneralJournalEntryDetailsDto>(
            "/api/accounting/general-journal-entries/" + journal.Document.Id, Json))!;
        Assert.Equal(journal.Document.Id, typed.Document.Id);
    }

    private static async Task<NgbSearchResult> SearchAsync(HttpClient client, string query, string suffix = "")
    {
        using var response = await client.GetAsync(Path + "?query=" + Uri.EscapeDataString(query) + suffix);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<NgbSearchResult>(Json))!;
    }

    private sealed class UnavailableReports : IAuthorizedReportReadService
    {
        public Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct)
            => throw new IOException("Private provider details must not enter search results.");

        public Task<ReportDefinitionDto> GetDefinitionAsync(string reportCode, CancellationToken ct)
            => throw new InvalidOperationException("Search must only discover report definitions.");

        public Task<ReportExecutionResponseDto> ExecuteAsync(
            string reportCode, ReportExecutionRequestDto request, CancellationToken ct)
            => throw new InvalidOperationException("Search must not execute reports.");
    }
}
