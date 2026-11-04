using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Reporting;
using NGB.Contracts.Security;
using NGB.Core.Security;
using NGB.Persistence.Security;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class ReportVariantHttpTests
{
    private const string Path = "/api/reports/" + InspectionReports.Summary + "/variants";

    [Fact]
    public async Task Private_variants_are_owned_by_the_current_user_and_shared_mutations_require_their_own_permission()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("administrator", true, InspectionHost.AdministratorRoleCode, "Renamed administrator");
        await host.LinkUserAsync("reader", true, "example.variant-reader", "Report reader");
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        using var deniedList = await reader.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Forbidden, deniedList.StatusCode);
        await GrantAsync(host, admin, NgbPermissionActions.View, NgbPermissionActions.Execute,
            NgbPermissionActions.SavePrivateVariant, NgbPermissionActions.DeleteVariant);

        var adminPrivate = await SaveAsync(admin, Variant("personal", "Admin-only result filter"));
        var shared = await SaveAsync(admin, Variant("shared", "Shared inspection summary") with { IsShared = true });
        var visible = await reader.GetFromJsonAsync<ReportVariantDto[]>(Path, Json);
        Assert.Equal(shared.VariantCode, Assert.Single(visible!).VariantCode);
        using var hidden = await reader.GetAsync(Path + "/personal");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var hiddenDelete = await reader.DeleteAsync(Path + "/personal");
        Assert.Equal(HttpStatusCode.NotFound, hiddenDelete.StatusCode);
        var readerPrivate = await SaveAsync(reader, Variant("personal", "Reader-only result filter"));
        Assert.Equal(adminPrivate.Name, (await admin.GetFromJsonAsync<ReportVariantDto>(Path + "/personal", Json))!.Name);
        Assert.Equal(readerPrivate.Name, (await reader.GetFromJsonAsync<ReportVariantDto>(Path + "/personal", Json))!.Name);

        using var sharedUpdate = await reader.PutAsJsonAsync(Path + "/shared", shared with { Name = "Unauthorized change" });
        using var sharedDelete = await reader.DeleteAsync(Path + "/shared");
        Assert.Equal(HttpStatusCode.Forbidden, sharedUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sharedDelete.StatusCode);
        Assert.Equal(shared.Name, (await admin.GetFromJsonAsync<ReportVariantDto>(Path + "/shared", Json))!.Name);
        using var conflict = await admin.PutAsJsonAsync(Path + "/personal", adminPrivate with { IsShared = true });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var deleted = await reader.DeleteAsync(Path + "/personal");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(adminPrivate.Name, (await admin.GetFromJsonAsync<ReportVariantDto>(Path + "/personal", Json))!.Name);
        await host.DeactivateUserAsync("reader");
        using var inactive = await reader.PutAsJsonAsync(Path + "/blocked", Variant("blocked", "Blocked"));
        Assert.Equal(HttpStatusCode.Forbidden, inactive.StatusCode);
    }

    [Fact]
    public async Task Defaults_validation_route_identity_and_execution_preserve_the_saved_report_contract()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var definition = await admin.GetFromJsonAsync<ReportDefinitionDto>("/api/report-definitions/" + InspectionReports.Summary, Json);
        Assert.True(definition!.Capabilities!.AllowsVariants);
        var (documentPath, version) = await CreateCompleteInspectionAsync(admin);
        using var posted = await ExecutePostAsync(admin, documentPath, version, "variant-inspection-post");
        await EnsureSuccessAsync(posted);
        var first = await SaveAsync(admin, Variant("first", "First") with { IsDefault = true });
        var second = await SaveAsync(admin, Variant("second", "Second") with { IsDefault = true });
        var variants = await admin.GetFromJsonAsync<ReportVariantDto[]>(Path, Json);
        Assert.Equal(second.VariantCode, Assert.Single(variants!, variant => variant.IsDefault).VariantCode);
        Assert.False(Assert.Single(variants!, variant => variant.VariantCode == first.VariantCode).IsDefault);
        using var routeIdentity = await admin.PutAsJsonAsync(Path + "/route-owned",
            Variant("ignored-code", "Route identity") with { ReportCode = "ignored-report" });
        await EnsureSuccessAsync(routeIdentity);
        var saved = (await routeIdentity.Content.ReadFromJsonAsync<ReportVariantDto>(Json))!;
        Assert.Equal("route-owned", saved.VariantCode);
        Assert.Equal(InspectionReports.Summary, saved.ReportCode);

        var before = await admin.GetStringAsync(Path + "/first");
        using var invalid = await admin.PutAsJsonAsync(Path + "/first", first with { Name = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(before, await admin.GetStringAsync(Path + "/first"));
        using var oversized = await admin.PutAsJsonAsync(Path + "/first",
            first with { Name = new string('x', ReportVariantLimits.MaxNameLength + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Equal(before, await admin.GetStringAsync(Path + "/first"));

        var response = await PostAsync<ReportExecutionResponseDto>(admin,
            "/api/reports/" + InspectionReports.Summary + "/execute", new ReportExecutionRequestDto(VariantCode: "second"));
        var group = Assert.Single(response.Sheet.Rows, row => row.RowKind == ReportRowKind.Group);
        Assert.Equal("Passed", group.Cells[0].Display);
        Assert.Equal(1m, group.Cells[^1].Value!.Value.GetDecimal());
        Assert.Equal(1m, Assert.Single(response.Sheet.Rows, row => row.RowKind == ReportRowKind.Total).Cells[^1].Value!.Value.GetDecimal());
        using var missing = await admin.PostAsJsonAsync("/api/reports/" + InspectionReports.Summary + "/execute",
            new ReportExecutionRequestDto(VariantCode: "missing"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var anonymous = host.CreateAnonymousClient();
        using var unauthenticated = await anonymous.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    private static ReportVariantDto Variant(string code, string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new ReportVariantDto(code, InspectionReports.Summary, name,
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["result"] = new(JsonSerializer.SerializeToElement("Passed"))
            },
            Parameters: new Dictionary<string, string>
            {
                ["from_utc"] = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["to_utc"] = today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
    }

    private static async Task<ReportVariantDto> SaveAsync(HttpClient client, ReportVariantDto variant)
    {
        using var response = await client.PutAsJsonAsync(Path + "/" + variant.VariantCode, variant);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<ReportVariantDto>(Json))!;
    }

    private static async Task GrantAsync(InspectionHost host, HttpClient admin, params string[] actions)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var role = await scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>().GetByCodeAsync("example.variant-reader");
        var request = new ReplaceRolePermissionsRequestDto(actions.Select(action =>
            new PermissionAssignmentDto(NgbResourceKinds.Report, InspectionReports.Summary, action)).ToArray());
        using var response = await admin.PutAsJsonAsync($"/api/security/roles/{role!.RoleId}/permissions", request);
        await EnsureSuccessAsync(response);
    }
}
