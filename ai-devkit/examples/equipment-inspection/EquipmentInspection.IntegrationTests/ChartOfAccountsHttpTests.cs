using System.Net;
using System.Net.Http.Json;
using NGB.Contracts.Admin;
using NGB.Contracts.Audit;
using NGB.Contracts.Security;
using NGB.Contracts.Services;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class ChartOfAccountsHttpTests
{
    private const string Path = "/api/chart-of-accounts";

    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Both_administrators_manage_accounts_and_retain_audited_lifecycle(string username)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode,
                "Renamed equipment supervisors");
        }

        using var admin = await host.CreateClientAsync(username);
        var metadata = (await admin.GetFromJsonAsync<ChartOfAccountsMetadataDto>(Path + "/metadata"))!;
        Assert.Contains(metadata.AccountTypeOptions, option => option.Value == "Asset");
        Assert.Contains(metadata.CashFlowRoleOptions, option => option.Value == "CashEquivalent");
        var account = await PostAsync<ChartOfAccountsAccountDto>(admin, Path,
            new ChartOfAccountsUpsertRequestDto("TEST.CASH", "Inspection cash", "Asset", CashFlowRole: "CashEquivalent"));
        Assert.True(account.IsActive);
        Assert.False(account.IsDeleted);
        Assert.Equal("CashEquivalent", account.CashFlowRole);
        var path = $"{Path}/{account.AccountId}";
        using var update = await admin.PutAsJsonAsync(path,
            new ChartOfAccountsUpsertRequestDto("TEST.CASH", "Renamed inspection cash", "Asset",
                CashFlowRole: "CashEquivalent"));
        await EnsureSuccessAsync(update);
        Assert.Equal("Renamed inspection cash", (await update.Content.ReadFromJsonAsync<ChartOfAccountsAccountDto>())!.Name);
        var lookup = await PostAsync<LookupItemDto[]>(admin, Path + "/by-ids",
            new ByIdsRequestDto([account.AccountId, account.AccountId, Guid.Empty, Guid.NewGuid()]));
        Assert.Equal(account.AccountId, Assert.Single(lookup).Id);
        Assert.Contains("Renamed inspection cash", lookup[0].Label);

        using var deactivate = await admin.PostAsync(path + "/set-active?isActive=false", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.False((await admin.GetFromJsonAsync<ChartOfAccountsAccountDto>(path))!.IsActive);
        using var reactivate = await admin.PostAsync(path + "/set-active?isActive=true", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        using var mark = await admin.PostAsync(path + "/mark-for-deletion", null);
        Assert.Equal(HttpStatusCode.NoContent, mark.StatusCode);
        var marked = (await admin.GetFromJsonAsync<ChartOfAccountsAccountDto>(path))!;
        Assert.True(marked.IsDeleted);
        Assert.True(marked.IsMarkedForDeletion);
        Assert.False(marked.IsActive);
        var hidden = (await admin.GetFromJsonAsync<ChartOfAccountsPageDto>(Path + "?search=TEST.CASH"))!;
        Assert.Empty(hidden.Items);
        var deleted = (await admin.GetFromJsonAsync<ChartOfAccountsPageDto>(
            Path + "?search=TEST.CASH&includeDeleted=true&onlyDeleted=true"))!;
        Assert.Equal(account.AccountId, Assert.Single(deleted.Items).AccountId);
        using var unmark = await admin.PostAsync(path + "/unmark-for-deletion", null);
        Assert.Equal(HttpStatusCode.NoContent, unmark.StatusCode);
        var restored = (await admin.GetFromJsonAsync<ChartOfAccountsAccountDto>(path))!;
        Assert.False(restored.IsDeleted);
        Assert.False(restored.IsActive);

        var audit = (await admin.GetFromJsonAsync<AuditLogPageDto>(
            $"/api/audit/entities/ChartOfAccountsAccount/{account.AccountId}?limit=100", Json))!;
        foreach (var action in new[] { "create", "update", "set_active", "mark_for_deletion", "unmark_for_deletion" })
            Assert.Contains(audit.Items, item => item.ActionCode == "coa.account." + action);
    }

    [Fact]
    public async Task Account_pages_bind_cursors_to_filters_and_validate_public_requests()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        foreach (var code in new[] { "PAGE.A", "PAGE.B", "PAGE.C" })
        {
            await PostAsync<ChartOfAccountsAccountDto>(admin, Path,
                new ChartOfAccountsUpsertRequestDto(code, "Paged account " + code, "Asset"));
        }
        var query = Path + "?search=PAGE.&accountTypes=Asset&onlyActive=true&limit=1";
        var first = (await admin.GetFromJsonAsync<ChartOfAccountsPageDto>(query))!;
        Assert.Equal("PAGE.A", Assert.Single(first.Items).Code);
        Assert.Equal(3, first.Total);
        Assert.False(string.IsNullOrWhiteSpace(first.NextCursor));
        var second = (await admin.GetFromJsonAsync<ChartOfAccountsPageDto>(
            query + "&cursor=" + Uri.EscapeDataString(first.NextCursor!)))!;
        Assert.Equal("PAGE.B", Assert.Single(second.Items).Code);
        var third = (await admin.GetFromJsonAsync<ChartOfAccountsPageDto>(
            query + "&cursor=" + Uri.EscapeDataString(second.NextCursor!)))!;
        Assert.Equal("PAGE.C", Assert.Single(third.Items).Code);
        Assert.Null(third.NextCursor);
        using var changedFilter = await admin.GetAsync(query.Replace("onlyActive=true", "onlyActive=false")
            + "&cursor=" + Uri.EscapeDataString(first.NextCursor!));
        Assert.Equal(HttpStatusCode.BadRequest, changedFilter.StatusCode);
        foreach (var invalid in new[] { "limit=0", "limit=501", "offset=-1", "accountTypes=unknown", "cursor=invalid" })
        {
            using var response = await admin.GetAsync(Path + "?" + invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var request in new[]
        {
            new ChartOfAccountsUpsertRequestDto("INVALID.TYPE", "Invalid type", "unknown"),
            new ChartOfAccountsUpsertRequestDto("INVALID.ROLE", "Invalid role", "Asset", CashFlowRole: "unknown"),
            new ChartOfAccountsUpsertRequestDto("INVALID.CASH", "Invalid cash", "Expense", CashFlowRole: "CashEquivalent"),
            new ChartOfAccountsUpsertRequestDto("INVALID.LINE", "Missing line", "Asset", CashFlowRole: "WorkingCapital")
        })
        {
            using var response = await admin.PostAsJsonAsync(Path, request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task View_permission_does_not_grant_management_and_other_actors_remain_denied()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("reader", true, "inspection.account-reader", "Account reader");
        await host.LinkUserAsync("imitator", true, "inspection.account-observer", "Administrator");
        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode, "Administrator");
        using var admin = await host.CreateClientAsync("administrator");
        var account = await PostAsync<ChartOfAccountsAccountDto>(admin, Path,
            new ChartOfAccountsUpsertRequestDto("ACCESS.A", "Access account", "Asset"));
        var path = $"{Path}/{account.AccountId}";
        using var reader = await host.CreateClientAsync("reader");
        using var ungranted = await reader.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Forbidden, ungranted.StatusCode);
        var access = (await reader.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access"))!;
        var role = await PostAsync<RoleDetailsDto>(admin, "/api/security/roles",
            new CreateRoleRequestDto("inspection.account-viewer", "Account viewer", null,
                [new PermissionAssignmentDto("admin", "chart_of_accounts", "view")]));
        using var assign = await admin.PutAsJsonAsync($"/api/security/users/{access.UserId}/roles",
            new ReplaceUserRolesRequestDto([role.RoleId]));
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        foreach (var endpoint in new[] { Path, Path + "/metadata", path })
        {
            using var response = await reader.GetAsync(endpoint);
            await EnsureSuccessAsync(response);
        }
        var lookup = await PostAsync<LookupItemDto[]>(reader, Path + "/by-ids", new ByIdsRequestDto([account.AccountId]));
        Assert.Equal(account.AccountId, Assert.Single(lookup).Id);
        using var create = await reader.PostAsJsonAsync(Path, new ChartOfAccountsUpsertRequestDto("DENIED.A", "Denied", "Asset"));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using var update = await reader.PutAsJsonAsync(path, new ChartOfAccountsUpsertRequestDto("ACCESS.A", "Denied", "Asset"));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        foreach (var operation in new[] { "mark-for-deletion", "unmark-for-deletion", "set-active?isActive=false" })
        {
            using var response = await reader.PostAsync(path + "/" + operation, null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        foreach (var username in new[] { "imitator", "inactive-administrator" })
        {
            using var actor = await host.CreateClientAsync(username);
            using var read = await actor.GetAsync(Path);
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
            using var mutation = await actor.PostAsync(path + "/mark-for-deletion", null);
            Assert.Equal(HttpStatusCode.Forbidden, mutation.StatusCode);
        }
        using var anonymous = host.CreateAnonymousClient();
        using var denied = await anonymous.GetAsync(Path);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(account, await admin.GetFromJsonAsync<ChartOfAccountsAccountDto>(path));
    }
}
