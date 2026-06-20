using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Api.Models;
using NGB.Accounting.Documents;
using NGB.Contracts.Accounting;
using NGB.Contracts.Admin;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Security;
using NGB.Core.AuditLog;
using NGB.Core.Reporting;
using NGB.Core.Security;
using NGB.PropertyManagement.Definitions;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.PostgreSql.Bootstrap;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmSecurityAccessManagement_Http_P0Tests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    public async Task InitializeAsync() => await fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SecurityEndpoints_UseNgbDatabaseRolesAndDenyDirectAdminRoutesForLimitedUsers()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var payablesRole = (await GetRolesAsync(adminClient))
            .Should()
            .ContainSingle(role => role.Code == "pm-ap-clerk")
            .Subject;

        var (limitedEmail, limitedPassword, _) = await CreateUserAsync(
            adminClient,
            "payables-clerk",
            "Payables Clerk",
            [payablesRole.RoleId]);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));

        var access = await GetCurrentAccessAsync(limitedClient);
        access.IsBootstrapAdmin.Should().BeFalse();
        access.Roles.Should().ContainSingle(role => role.Code == "pm-ap-clerk");
        access.Permissions.Should().Contain(permission =>
            permission.ResourceKind == "document"
            && permission.ResourceCode == "pm.payable_charge"
            && permission.ActionCode == "view");
        access.Permissions.Should().NotContain(permission =>
            permission.ResourceKind == "system"
            && permission.ResourceCode == "users"
            && permission.ActionCode == "view");

        using var usersResponse = await limitedClient.GetAsync("/api/security/users");
        usersResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var usersProblem = await usersResponse.Content.ReadAsStringAsync();
        usersProblem.Should().Contain("permission_denied");
        usersProblem.Should().Contain("system");
        usersProblem.Should().Contain("users");

        using var rolesResponse = await limitedClient.GetAsync("/api/security/roles");
        rolesResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var menu = await limitedClient.GetFromJsonAsync<MainMenuDto>("/api/main-menu");
        menu.Should().NotBeNull();
        var menuLabels = menu!.Groups.SelectMany(group => group.Items).Select(item => item.Label).ToArray();
        menuLabels.Should().NotContain("Users");
        menuLabels.Should().NotContain("Roles & Permissions");
    }

    [Fact]
    public async Task MainMenu_WhenUserHasBalanceSheetPostingLogAndExternalTools_ShowsOnlyThoseItems()
    {
        await using var factory = new PmApiFactory(fixture, new Dictionary<string, string?>
        {
            [$"{nameof(ExternalLinksSettings)}:{nameof(ExternalLinksSettings.HealthUiUrl)}"] = "https://localhost:7075/health-ui",
            [$"{nameof(ExternalLinksSettings)}:{nameof(ExternalLinksSettings.BackgroundJobsUiUrl)}"] = "https://localhost:7074/hangfire"
        });
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-menu-probe-{Guid.NewGuid():N}",
            "Menu Probe",
            [
                new PermissionAssignmentDto(NgbResourceKinds.Report, AccountingReportCodes.BalanceSheet, NgbPermissionActions.View),
                new PermissionAssignmentDto(NgbResourceKinds.Report, AccountingReportCodes.BalanceSheet, NgbPermissionActions.Execute),
                new PermissionAssignmentDto(NgbResourceKinds.Report, AccountingReportCodes.PostingLog, NgbPermissionActions.View),
                new PermissionAssignmentDto(NgbResourceKinds.Report, AccountingReportCodes.PostingLog, NgbPermissionActions.Execute),
                new PermissionAssignmentDto(NgbResourceKinds.Admin, NgbPermissionResources.PostingLog, NgbPermissionActions.View),
                new PermissionAssignmentDto(NgbResourceKinds.External, PropertyManagementCodes.Watchdog, NgbPermissionActions.View),
                new PermissionAssignmentDto(NgbResourceKinds.External, PropertyManagementCodes.BackgroundJobs, NgbPermissionActions.View)
            ]);

        var (email, password, _) = await CreateUserAsync(
            adminClient,
            "menu-probe",
            "Menu Probe",
            [role.RoleId]);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(email, password));

        var menu = await limitedClient.GetFromJsonAsync<MainMenuDto>("/api/main-menu");
        menu.Should().NotBeNull();

        var items = menu!.Groups.SelectMany(group => group.Items).ToArray();
        items.Select(item => item.Label).Should().Contain([
            "Balance Sheet",
            "Posting Log",
            "Health",
            "Background Jobs"
        ]);
        items.Select(item => item.Label).Should().NotContain([
            "Period Close",
            "Integrity Checks",
            "Users",
            "Roles & Permissions"
        ]);
        items.Should().ContainSingle(item =>
            item.Label == "Posting Log"
            && item.Kind == NgbResourceKinds.Admin
            && item.Code == AccountingReportCodes.PostingLog
            && item.Route == "/admin/accounting/posting-log");
    }

    [Fact]
    public async Task MainMenu_EachCanonicalPermissionMapsToExactlyItsOwnItem()
    {
        await using var factory = new PmApiFactory(fixture, new Dictionary<string, string?>
        {
            [$"{nameof(ExternalLinksSettings)}:{nameof(ExternalLinksSettings.HealthUiUrl)}"] = "https://localhost:7075/health-ui",
            [$"{nameof(ExternalLinksSettings)}:{nameof(ExternalLinksSettings.BackgroundJobsUiUrl)}"] = "https://localhost:7074/hangfire"
        });
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);
        var role = await CreateRoleAsync(
            adminClient,
            $"pm-menu-mapping-{Guid.NewGuid():N}",
            "Menu Mapping",
            []);
        var (email, password, _) = await CreateUserAsync(
            adminClient,
            "menu-mapping",
            "Menu Mapping",
            [role.RoleId]);
        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(email, password));

        var cases = new[]
        {
            new MenuPermissionMapping(
                new(NgbResourceKinds.Document, AccountingDocumentTypeCodes.GeneralJournalEntry, NgbPermissionActions.View),
                NgbResourceKinds.Document,
                AccountingDocumentTypeCodes.GeneralJournalEntry,
                "/accounting/general-journal-entries"),
            new MenuPermissionMapping(
                new(NgbResourceKinds.Catalog, PropertyManagementCodes.Party, NgbPermissionActions.View),
                NgbResourceKinds.Catalog,
                PropertyManagementCodes.Party,
                $"/catalogs/{PropertyManagementCodes.Party}"),
            new MenuPermissionMapping(
                new(NgbResourceKinds.Report, AccountingReportCodes.BalanceSheet, NgbPermissionActions.View),
                NgbResourceKinds.Page,
                AccountingReportCodes.BalanceSheet,
                $"/reports/{AccountingReportCodes.BalanceSheet}"),
            new MenuPermissionMapping(
                new(NgbResourceKinds.Page, PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage, NgbPermissionActions.View),
                NgbResourceKinds.Page,
                PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage,
                "/receivables/open-items"),
            new MenuPermissionMapping(
                NgbSystemPermissions.UsersView,
                NgbResourceKinds.Page,
                "system.users",
                "/admin/security/users"),
            new MenuPermissionMapping(
                NgbSystemPermissions.RolesView,
                NgbResourceKinds.Page,
                "system.roles",
                "/admin/security/roles"),
            new MenuPermissionMapping(
                NgbSystemPermissions.ChartOfAccountsView,
                NgbResourceKinds.Admin,
                "chart-of-accounts",
                "/admin/chart-of-accounts"),
            new MenuPermissionMapping(
                NgbSystemPermissions.PeriodClosingView,
                NgbResourceKinds.Admin,
                "accounting.period_closing",
                "/admin/accounting/period-closing"),
            new MenuPermissionMapping(
                NgbSystemPermissions.PostingLogView,
                NgbResourceKinds.Admin,
                AccountingReportCodes.PostingLog,
                "/admin/accounting/posting-log"),
            new MenuPermissionMapping(
                NgbSystemPermissions.IntegrityView,
                NgbResourceKinds.Admin,
                AccountingReportCodes.Consistency,
                "/admin/accounting/consistency"),
            new MenuPermissionMapping(
                new(NgbResourceKinds.External, PropertyManagementCodes.Watchdog, NgbPermissionActions.View),
                NgbResourceKinds.External,
                PropertyManagementCodes.Watchdog,
                "https://localhost:7075/health-ui"),
            new MenuPermissionMapping(
                new(NgbResourceKinds.External, PropertyManagementCodes.BackgroundJobs, NgbPermissionActions.View),
                NgbResourceKinds.External,
                PropertyManagementCodes.BackgroundJobs,
                "https://localhost:7074/hangfire")
        };

        foreach (var mapping in cases)
        {
            using (var replaceResponse = await adminClient.PutAsJsonAsync(
                       $"/api/security/roles/{role.RoleId}/permissions",
                       new ReplaceRolePermissionsRequestDto(
                       [
                           new PermissionAssignmentDto(
                               mapping.Permission.ResourceKind,
                               mapping.Permission.ResourceCode,
                               mapping.Permission.ActionCode)
                       ])))
            {
                replaceResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, mapping.Permission.ToString());
            }

            var currentAccess = await GetCurrentAccessAsync(limitedClient);
            currentAccess.Permissions.Should().ContainSingle(permission =>
                permission.ResourceKind == mapping.Permission.ResourceKind
                && permission.ResourceCode == mapping.Permission.ResourceCode
                && permission.ActionCode == mapping.Permission.ActionCode,
                mapping.Permission.ToString());

            var menu = await limitedClient.GetFromJsonAsync<MainMenuDto>("/api/main-menu");
            menu.Should().NotBeNull();
            var items = menu!.Groups.SelectMany(group => group.Items).ToArray();

            items.Should().ContainSingle(mapping.Permission.ToString());
            items.Should().ContainSingle(item =>
                item.Kind == mapping.ExpectedKind
                && item.Code == mapping.ExpectedCode
                && item.Route == mapping.ExpectedRoute,
                mapping.Permission.ToString());
        }
    }

    [Theory]
    [InlineData(NgbPermissionResources.PeriodClosing, "Period Close", "/admin/accounting/period-closing", "Posting Log", "Integrity Checks")]
    [InlineData(NgbPermissionResources.PostingLog, "Posting Log", "/admin/accounting/posting-log", "Period Close", "Integrity Checks")]
    [InlineData(NgbPermissionResources.Integrity, "Integrity Checks", "/admin/accounting/consistency", "Period Close", "Posting Log")]
    public async Task MainMenu_AdminDiagnosticsUseDistinctAdminPermissions(
        string adminResource,
        string allowedLabel,
        string allowedRoute,
        string hiddenLabelA,
        string hiddenLabelB)
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-admin-menu-{Guid.NewGuid():N}",
            $"Admin Menu {adminResource}",
            [
                new PermissionAssignmentDto(NgbResourceKinds.Admin, adminResource, NgbPermissionActions.View)
            ]);

        var (email, password, _) = await CreateUserAsync(
            adminClient,
            $"admin-menu-{adminResource.Replace('_', '-')}",
            $"Admin Menu {adminResource}",
            [role.RoleId]);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(email, password));

        var menu = await limitedClient.GetFromJsonAsync<MainMenuDto>("/api/main-menu");
        menu.Should().NotBeNull();

        var setupItems = menu!.Groups
            .Should()
            .ContainSingle(group => group.Label == "Setup & Controls")
            .Subject
            .Items;

        setupItems.Should().ContainSingle(item =>
            item.Label == allowedLabel
            && item.Route == allowedRoute);
        setupItems.Select(item => item.Label).Should().NotContain([hiddenLabelA, hiddenLabelB]);
    }

    [Theory]
    [InlineData(NgbPermissionResources.PostingLog, AccountingReportCodes.PostingLog, AccountingReportCodes.Consistency)]
    [InlineData(NgbPermissionResources.Integrity, AccountingReportCodes.Consistency, AccountingReportCodes.PostingLog)]
    public async Task AdminBackedDiagnosticPermission_AuthorizesOnlyItsMatchingReport(
        string adminResource,
        string allowedReportCode,
        string deniedReportCode)
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-diagnostics-{Guid.NewGuid():N}",
            "Diagnostics",
            [
                new PermissionAssignmentDto(
                    NgbResourceKinds.Admin,
                    adminResource,
                    NgbPermissionActions.View)
            ]);

        var (email, password, _) = await CreateUserAsync(
            adminClient,
            "diagnostics",
            "Diagnostics User",
            [role.RoleId]);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(email, password));

        var definitions = await limitedClient.GetFromJsonAsync<IReadOnlyList<ReportDefinitionDto>>(
            "/api/report-definitions",
            Json);
        definitions.Should().NotBeNull();
        definitions!.Select(definition => definition.ReportCode).Should().ContainSingle().Which.Should().Be(allowedReportCode);

        using (var response = await limitedClient.GetAsync($"/api/report-definitions/{allowedReportCode}"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var response = await limitedClient.GetAsync($"/api/report-definitions/{deniedReportCode}"))
        {
            await AssertPermissionDeniedAsync(response, NgbResourceKinds.Report, deniedReportCode);
        }

        var request = string.Equals(allowedReportCode, AccountingReportCodes.Consistency, StringComparison.Ordinal)
            ? new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["period_utc"] = "2026-01-01"
                       },
                       Offset: 0,
                       Limit: 10)
            : new ReportExecutionRequestDto(Offset: 0, Limit: 10);

        using (var response = await limitedClient.PostAsJsonAsync(
                   $"/api/reports/{allowedReportCode}/execute",
                   request))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task PeriodClosingEndpoints_DenyEveryReadAndMutationWithoutItsCanonicalPermission()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);
        var role = await CreateRoleAsync(
            adminClient,
            $"pm-period-denied-{Guid.NewGuid():N}",
            "Period Denied",
            [
                new PermissionAssignmentDto(
                    NgbResourceKinds.Report,
                    AccountingReportCodes.BalanceSheet,
                    NgbPermissionActions.View)
            ]);
        var (email, password, _) = await CreateUserAsync(
            adminClient,
            "period-denied",
            "Period Denied",
            [role.RoleId]);
        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(email, password));

        var readRequests = new[]
        {
            "/api/accounting/period-closing/month?period=2026-01-01",
            "/api/accounting/period-closing/calendar?year=2026",
            "/api/accounting/period-closing/fiscal-year?fiscalYearEndPeriod=2026-12-01",
            "/api/accounting/period-closing/retained-earnings-accounts?q=retained&limit=10"
        };

        foreach (var request in readRequests)
        {
            using var response = await limitedClient.GetAsync(request);
            await AssertPermissionDeniedAsync(
                response,
                NgbResourceKinds.Admin,
                NgbPermissionResources.PeriodClosing,
                NgbPermissionActions.View);
        }

        var mutationRequests = new (string Route, object Body, string Action)[]
        {
            ("/api/accounting/period-closing/month/close", new CloseMonthRequestDto(new DateOnly(2026, 1, 1)), NgbPermissionActions.CloseMonth),
            ("/api/accounting/period-closing/month/reopen", new ReopenMonthRequestDto(new DateOnly(2026, 1, 1), "Security probe"), NgbPermissionActions.ReopenMonth),
            ("/api/accounting/period-closing/fiscal-year/close", new CloseFiscalYearRequestDto(new DateOnly(2026, 12, 1), Guid.Empty), NgbPermissionActions.CloseFiscalYear),
            ("/api/accounting/period-closing/fiscal-year/reopen", new ReopenFiscalYearRequestDto(new DateOnly(2026, 12, 1), "Security probe"), NgbPermissionActions.ReopenFiscalYear)
        };

        foreach (var request in mutationRequests)
        {
            using var response = await limitedClient.PostAsJsonAsync(request.Route, request.Body);
            await AssertPermissionDeniedAsync(
                response,
                NgbResourceKinds.Admin,
                NgbPermissionResources.PeriodClosing,
                request.Action);
        }
    }

    [Fact]
    public async Task PeriodClosingDefinitionsAndAdministratorRole_ContainEveryEndpointPermission()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);
        var expectedActions = new[]
        {
            NgbPermissionActions.View,
            NgbPermissionActions.CloseMonth,
            NgbPermissionActions.ReopenMonth,
            NgbPermissionActions.CloseFiscalYear,
            NgbPermissionActions.ReopenFiscalYear
        };

        var definitions = await adminClient.GetFromJsonAsync<PermissionDefinitionDto[]>(
            "/api/security/permissions/definitions");
        definitions.Should().NotBeNull();
        definitions!
            .Where(definition =>
                definition.ResourceKind == NgbResourceKinds.Admin
                && definition.ResourceCode == NgbPermissionResources.PeriodClosing)
            .Select(definition => definition.ActionCode)
            .Should()
            .BeEquivalentTo(expectedActions);

        var administrator = (await GetRolesAsync(adminClient))
            .Should()
            .ContainSingle(role => role.Code == "pm-administrator")
            .Subject;
        var details = await adminClient.GetFromJsonAsync<RoleDetailsDto>(
            $"/api/security/roles/{administrator.RoleId}");
        details.Should().NotBeNull();
        details!.Permissions
            .Where(permission =>
                permission.ResourceKind == NgbResourceKinds.Admin
                && permission.ResourceCode == NgbPermissionResources.PeriodClosing)
            .Select(permission => permission.ActionCode)
            .Should()
            .BeEquivalentTo(expectedActions);
    }

    [Fact]
    public async Task GeneralJournalEntryEndpoints_DenyEveryOperationWithoutItsCanonicalDocumentPermission()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);
        var role = await CreateRoleAsync(
            adminClient,
            $"pm-gje-denied-{Guid.NewGuid():N}",
            "GJE Denied",
            [
                new PermissionAssignmentDto(
                    NgbResourceKinds.Report,
                    AccountingReportCodes.BalanceSheet,
                    NgbPermissionActions.View)
            ]);
        var (email, password, _) = await CreateUserAsync(
            adminClient,
            "gje-denied",
            "GJE Denied",
            [role.RoleId]);
        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(email, password));

        var id = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var requests = new (HttpMethod Method, string Route, object? Body, string Action)[]
        {
            (HttpMethod.Get, "/api/accounting/general-journal-entries", null, NgbPermissionActions.View),
            (HttpMethod.Get, $"/api/accounting/general-journal-entries/{id}", null, NgbPermissionActions.View),
            (HttpMethod.Post, "/api/accounting/general-journal-entries", new CreateGeneralJournalEntryDraftRequestDto(DateTime.UtcNow), NgbPermissionActions.Create),
            (HttpMethod.Put, $"/api/accounting/general-journal-entries/{id}/header", new UpdateGeneralJournalEntryHeaderRequestDto("Security probe"), NgbPermissionActions.EditDraft),
            (HttpMethod.Put, $"/api/accounting/general-journal-entries/{id}/lines", new ReplaceGeneralJournalEntryLinesRequestDto("Security probe", []), NgbPermissionActions.EditDraft),
            (HttpMethod.Post, $"/api/accounting/general-journal-entries/{id}/submit", null, NgbPermissionActions.EditDraft),
            (HttpMethod.Post, $"/api/accounting/general-journal-entries/{id}/approve", null, NgbPermissionActions.Post),
            (HttpMethod.Post, $"/api/accounting/general-journal-entries/{id}/reject", new GeneralJournalEntryRejectRequestDto("Security probe"), NgbPermissionActions.Post),
            (HttpMethod.Post, $"/api/accounting/general-journal-entries/{id}/post", null, NgbPermissionActions.Post),
            (HttpMethod.Post, $"/api/accounting/general-journal-entries/{id}/reverse", new GeneralJournalEntryReverseRequestDto(DateTime.UtcNow), NgbPermissionActions.Unpost),
            (HttpMethod.Post, $"/api/accounting/general-journal-entries/{id}/mark-for-deletion", null, NgbPermissionActions.MarkForDeletion),
            (HttpMethod.Post, $"/api/accounting/general-journal-entries/{id}/unmark-for-deletion", null, NgbPermissionActions.UnmarkForDeletion),
            (HttpMethod.Get, $"/api/accounting/general-journal-entries/accounts/{accountId}", null, NgbPermissionActions.View)
        };

        foreach (var request in requests)
        {
            using var message = new HttpRequestMessage(request.Method, request.Route)
            {
                Content = request.Body is null ? null : JsonContent.Create(request.Body)
            };
            using var response = await limitedClient.SendAsync(message);
            await AssertPermissionDeniedAsync(
                response,
                NgbResourceKinds.Document,
                AccountingDocumentTypeCodes.GeneralJournalEntry,
                request.Action);
        }
    }

    [Fact]
    public async Task GeneralJournalEntryDefaults_AreAssignedOnlyToAccountingCapableRoles()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);
        var roles = await GetRolesAsync(adminClient);

        foreach (var roleCode in new[] { "pm-administrator", "pm-accountant", "pm-auditor", "pm-read-only" })
        {
            var role = roles.Should().ContainSingle(candidate => candidate.Code == roleCode).Subject;
            var details = await adminClient.GetFromJsonAsync<RoleDetailsDto>($"/api/security/roles/{role.RoleId}");
            details.Should().NotBeNull();

            var actions = details!.Permissions
                .Where(permission =>
                    permission.ResourceKind == NgbResourceKinds.Document
                    && permission.ResourceCode == AccountingDocumentTypeCodes.GeneralJournalEntry)
                .Select(permission => permission.ActionCode)
                .ToArray();

            actions.Should().Contain([NgbPermissionActions.View, NgbPermissionActions.Lookup], roleCode);
            if (roleCode is "pm-administrator" or "pm-accountant")
            {
                actions.Should().Contain(
                    [NgbPermissionActions.Create, NgbPermissionActions.EditDraft, NgbPermissionActions.Post, NgbPermissionActions.Unpost],
                    roleCode);
            }
            else
            {
                actions.Should().NotContain(
                    [NgbPermissionActions.Create, NgbPermissionActions.EditDraft, NgbPermissionActions.Post, NgbPermissionActions.Unpost],
                    roleCode);
            }
        }

        foreach (var roleCode in new[] { "pm-ar-clerk", "pm-ap-clerk", "pm-property-manager", "pm-maintenance-coordinator" })
        {
            var role = roles.Should().ContainSingle(candidate => candidate.Code == roleCode).Subject;
            var details = await adminClient.GetFromJsonAsync<RoleDetailsDto>($"/api/security/roles/{role.RoleId}");
            details.Should().NotBeNull();
            details!.Permissions.Should().NotContain(permission =>
                permission.ResourceKind == NgbResourceKinds.Document
                && permission.ResourceCode == AccountingDocumentTypeCodes.GeneralJournalEntry,
                roleCode);
        }
    }

    [Fact]
    public async Task RoleLifecycle_ChangesCurrentAccessForAssignedUsers()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-limited-report-{Guid.NewGuid():N}",
            "Limited Balance Sheet",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view"),
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "execute")
            ]);

        var (limitedEmail, limitedPassword, _) = await CreateUserAsync(
            adminClient,
            "limited-report",
            "Limited Reporter",
            [role.RoleId]);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));

        var before = await GetCurrentAccessAsync(limitedClient);
        before.Roles.Should().ContainSingle(x => x.RoleId == role.RoleId);
        before.Permissions.Should().Contain(permission =>
            permission.ResourceKind == "report"
            && permission.ResourceCode == "accounting.balance_sheet"
            && permission.ActionCode == "execute");

        using var deactivateResponse = await adminClient.PostAsync($"/api/security/roles/{role.RoleId}/deactivate", content: null);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDeactivate = await GetCurrentAccessAsync(limitedClient);
        afterDeactivate.AccessVersion.Should().BeGreaterThan(before.AccessVersion);
        afterDeactivate.Roles.Should().NotContain(x => x.RoleId == role.RoleId);
        afterDeactivate.Permissions.Should().NotContain(permission =>
            permission.ResourceKind == "report"
            && permission.ResourceCode == "accounting.balance_sheet");

        using var reactivateResponse = await adminClient.PostAsync($"/api/security/roles/{role.RoleId}/reactivate", content: null);
        reactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterReactivate = await GetCurrentAccessAsync(limitedClient);
        afterReactivate.AccessVersion.Should().BeGreaterThan(afterDeactivate.AccessVersion);
        afterReactivate.Roles.Should().ContainSingle(x => x.RoleId == role.RoleId);
        afterReactivate.Permissions.Should().Contain(permission =>
            permission.ResourceKind == "report"
            && permission.ResourceCode == "accounting.balance_sheet"
            && permission.ActionCode == "execute");
    }

    [Fact]
    public async Task DirectMetadataDocumentCatalogAndReportRoutes_EnforceLimitedDatabasePermissions()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-report-only-{Guid.NewGuid():N}",
            "Report Only",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view"),
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "execute")
            ]);

        var (limitedEmail, limitedPassword, _) = await CreateUserAsync(
            adminClient,
            "report-only",
            "Report Only User",
            [role.RoleId]);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));

        using (var response = await limitedClient.GetAsync("/api/catalogs/metadata"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var metadata = await response.Content.ReadFromJsonAsync<IReadOnlyList<CatalogTypeMetadataDto>>();
            metadata.Should().NotBeNull();
            metadata!.Select(x => x.CatalogType).Should().NotContain(PropertyManagementCodes.Party);
        }

        using (var response = await limitedClient.GetAsync($"/api/catalogs/{PropertyManagementCodes.Party}/metadata"))
        {
            await AssertPermissionDeniedAsync(response, "catalog", PropertyManagementCodes.Party);
        }

        using (var response = await limitedClient.GetAsync($"/api/catalogs/{PropertyManagementCodes.Party}"))
        {
            await AssertPermissionDeniedAsync(response, "catalog", PropertyManagementCodes.Party);
        }

        using (var response = await limitedClient.GetAsync("/api/documents/metadata"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var metadata = await response.Content.ReadFromJsonAsync<IReadOnlyList<DocumentTypeMetadataDto>>();
            metadata.Should().NotBeNull();
            metadata!.Select(x => x.DocumentType).Should().NotContain(PropertyManagementCodes.ReceivableCharge);
        }

        using (var response = await limitedClient.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableCharge}/metadata"))
        {
            await AssertPermissionDeniedAsync(response, "document", PropertyManagementCodes.ReceivableCharge);
        }

        using (var response = await limitedClient.GetAsync($"/api/documents/{PropertyManagementCodes.ReceivableCharge}"))
        {
            await AssertPermissionDeniedAsync(response, "document", PropertyManagementCodes.ReceivableCharge);
        }

        using (var response = await limitedClient.GetAsync("/api/report-definitions"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var definitions = await response.Content.ReadFromJsonAsync<IReadOnlyList<ReportDefinitionDto>>(Json);
            definitions.Should().NotBeNull();
            definitions!.Select(x => x.ReportCode)
                .Should()
                .Contain("accounting.balance_sheet")
                .And.NotContain("accounting.cash_flow_statement_indirect");
        }

        using (var response = await limitedClient.GetAsync("/api/report-definitions/accounting.balance_sheet"))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var response = await limitedClient.GetAsync("/api/report-definitions/accounting.cash_flow_statement_indirect"))
        {
            await AssertPermissionDeniedAsync(response, "report", "accounting.cash_flow_statement_indirect");
        }

        using (var response = await limitedClient.PostAsJsonAsync(
                   "/api/reports/accounting.cash_flow_statement_indirect/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-01-01",
                           ["to_utc"] = "2026-01-31"
                       },
                       Offset: 0,
                       Limit: 10)))
        {
            await AssertPermissionDeniedAsync(response, "report", "accounting.cash_flow_statement_indirect");
        }
    }

    [Fact]
    public async Task DeactivatedUser_CannotObtainUsableAccess()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-disabled-user-{Guid.NewGuid():N}",
            "Disabled User Probe",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view")
            ]);

        var (limitedEmail, limitedPassword, user) = await CreateUserAsync(
            adminClient,
            "disable-probe",
            "Disable Probe",
            [role.RoleId]);

        using (var activeClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword)))
        {
            var beforeDeactivate = await GetCurrentAccessAsync(activeClient);
            beforeDeactivate.IsActive.Should().BeTrue();
            beforeDeactivate.Permissions.Should().Contain(permission =>
                permission.ResourceKind == "report"
                && permission.ResourceCode == "accounting.balance_sheet"
                && permission.ActionCode == "view");
        }

        using (var response = await adminClient.PostAsync($"/api/security/users/{user.UserId}/deactivate", content: null))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var refreshed = await adminClient.GetFromJsonAsync<UserDetailsDto>($"/api/security/users/{user.UserId}");
        refreshed.Should().NotBeNull();
        refreshed!.IsActive.Should().BeFalse();
        refreshed.KeycloakEnabled.Should().BeFalse();

        var createClient = () => CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));
        HttpClient? disabledClient = null;
        try
        {
            disabledClient = createClient();
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using (disabledClient)
        {
            var access = await GetCurrentAccessAsync(disabledClient);
            access.IsActive.Should().BeFalse();
            access.Roles.Should().BeEmpty();
            access.Permissions.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task InactiveAssignedRole_RemainsVisibleInAdminButDoesNotContributeCurrentOrEffectiveAccess()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var role = await CreateRoleAsync(
            adminClient,
            $"pm-inactive-assigned-{Guid.NewGuid():N}",
            "Inactive Assigned",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view"),
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "execute")
            ]);

        var (limitedEmail, limitedPassword, user) = await CreateUserAsync(
            adminClient,
            "inactive-role",
            "Inactive Role User",
            [role.RoleId]);

        using (var response = await adminClient.PostAsync($"/api/security/roles/{role.RoleId}/deactivate", content: null))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var adminUser = await adminClient.GetFromJsonAsync<UserDetailsDto>($"/api/security/users/{user.UserId}");
        adminUser.Should().NotBeNull();
        adminUser!.Roles.Should().ContainSingle(x => x.RoleId == role.RoleId && x.IsActive == false);

        using var limitedClient = CreateHttpsClient(factory, new PmKeycloakTestUser(limitedEmail, limitedPassword));
        var currentAccess = await GetCurrentAccessAsync(limitedClient);
        currentAccess.IsActive.Should().BeTrue();
        currentAccess.Roles.Should().NotContain(x => x.RoleId == role.RoleId);
        currentAccess.Permissions.Should().NotContain(permission =>
            permission.ResourceKind == "report"
            && permission.ResourceCode == "accounting.balance_sheet");

        var effective = await adminClient.GetFromJsonAsync<EffectiveAccessDto>(
            $"/api/security/users/{user.UserId}/effective-access");

        effective.Should().NotBeNull();
        effective!.Groups
            .SelectMany(group => group.Resources)
            .Where(resource => resource.ResourceKind == "report" && resource.ResourceCode == "accounting.balance_sheet")
            .SelectMany(resource => resource.Actions)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task SecurityAudit_WritesBusinessRowsForUserRoleAndRolePermissionChanges()
    {
        await using var factory = new PmApiFactory(fixture);
        await SeedSecurityDefaultsAsync(factory);

        using var adminClient = CreateHttpsClient(factory);

        var roleA = await CreateRoleAsync(
            adminClient,
            $"pm-audit-a-{Guid.NewGuid():N}",
            "Audit Role A",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "view")
            ]);

        var roleB = await CreateRoleAsync(
            adminClient,
            $"pm-audit-b-{Guid.NewGuid():N}",
            "Audit Role B",
            [
                new PermissionAssignmentDto("report", "accounting.balance_sheet", "execute")
            ]);

        var (_, _, user) = await CreateUserAsync(
            adminClient,
            "audit-user",
            "Audit User",
            [roleA.RoleId]);

        using (var response = await adminClient.PutAsJsonAsync(
                   $"/api/security/users/{user.UserId}/roles",
                   new ReplaceUserRolesRequestDto([roleB.RoleId])))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var userEvents = await ReadAuditEventsAsync(
            factory,
            AuditEntityKind.SecurityUser,
            user.UserId,
            AuditActionCodes.SecurityUserRolesReplace);

        var userRolesEvent = userEvents.Should().ContainSingle().Subject;
        var userRolesChange = GetChange(userRolesEvent, "roles");
        ReadObjectArrayProperty(userRolesChange.OldValueJson, "code").Should().Contain(roleA.Code).And.NotContain(roleB.Code);
        ReadObjectArrayProperty(userRolesChange.NewValueJson, "code").Should().Contain(roleB.Code).And.NotContain(roleA.Code);

        var roleARemovalEvents = await ReadAuditEventsAsync(
            factory,
            AuditEntityKind.SecurityRole,
            roleA.RoleId,
            AuditActionCodes.SecurityRoleUpdate);
        var roleARemoval = roleARemovalEvents
            .Select(e => GetChange(e, "assigned_users"))
            .Single(change => change.OldValueJson is not null && change.NewValueJson is null);
        ReadObjectProperty(roleARemoval.OldValueJson, "email").Should().Be(user.Email);

        var roleBAdditionEvents = await ReadAuditEventsAsync(
            factory,
            AuditEntityKind.SecurityRole,
            roleB.RoleId,
            AuditActionCodes.SecurityRoleUpdate);
        var roleBAddition = roleBAdditionEvents
            .Select(e => GetChange(e, "assigned_users"))
            .Single(change => change.OldValueJson is null && change.NewValueJson is not null);
        ReadObjectProperty(roleBAddition.NewValueJson, "email").Should().Be(user.Email);

        using (var response = await adminClient.PutAsJsonAsync(
                   $"/api/security/roles/{roleB.RoleId}/permissions",
                   new ReplaceRolePermissionsRequestDto(
                   [
                       new PermissionAssignmentDto("report", "accounting.balance_sheet", "view"),
                       new PermissionAssignmentDto("report", "accounting.balance_sheet", "export")
                   ])))
        {
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var permissionEvents = await ReadAuditEventsAsync(
            factory,
            AuditEntityKind.SecurityRole,
            roleB.RoleId,
            AuditActionCodes.SecurityRolePermissionsReplace);

        var permissionsChange = GetChange(permissionEvents.Should().ContainSingle().Subject, "permissions");
        ReadObjectArrayProperty(permissionsChange.OldValueJson, "key")
            .Should()
            .Contain("report.accounting.balance_sheet.execute")
            .And.NotContain("report.accounting.balance_sheet.export");
        ReadObjectArrayProperty(permissionsChange.NewValueJson, "key")
            .Should()
            .Contain("report.accounting.balance_sheet.view")
            .And.Contain("report.accounting.balance_sheet.export")
            .And.NotContain("report.accounting.balance_sheet.execute");
    }

    private static HttpClient CreateHttpsClient(PmApiFactory factory, PmKeycloakTestUser? user = null)
        => factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") },
            user);

    private static async Task<IReadOnlyList<RoleListItemDto>> GetRolesAsync(HttpClient client)
    {
        var roles = await client.GetFromJsonAsync<RoleListItemDto[]>("/api/security/roles");
        roles.Should().NotBeNull();
        return roles!;
    }

    private static async Task<RoleDetailsDto> CreateRoleAsync(
        HttpClient client,
        string code,
        string name,
        IReadOnlyList<PermissionAssignmentDto> permissions)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/security/roles",
            new CreateRoleRequestDto(code, name, "Integration-test role.", permissions));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var role = await response.Content.ReadFromJsonAsync<RoleDetailsDto>();
        role.Should().NotBeNull();
        return role!;
    }

    private static async Task<(string Email, string Password, UserDetailsDto User)> CreateUserAsync(
        HttpClient client,
        string localPartPrefix,
        string displayName,
        IReadOnlyList<Guid> roleIds)
    {
        var email = $"{localPartPrefix}-{Guid.NewGuid():N}@integration.test";
        var password = "Ngb#2026-Strong";
        var nameParts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        using var response = await client.PostAsJsonAsync(
            "/api/security/users",
            new CreateUserRequestDto(
                email,
                FirstName: nameParts.FirstOrDefault() ?? displayName,
                LastName: nameParts.Skip(1).FirstOrDefault() ?? "User",
                DisplayName: displayName,
                Enabled: true,
                TemporaryPassword: password,
                RequirePasswordUpdate: false,
                roleIds));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<UserDetailsDto>();
        user.Should().NotBeNull();
        user!.Email.Should().Be(email);
        user.Roles.Select(role => role.RoleId).Should().BeEquivalentTo(roleIds);

        return (email, password, user);
    }

    private static async Task<CurrentAccessDto> GetCurrentAccessAsync(HttpClient client)
    {
        var access = await client.GetFromJsonAsync<CurrentAccessDto>("/api/security/me/access");
        access.Should().NotBeNull();
        return access!;
    }

    private static async Task SeedSecurityDefaultsAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PropertyManagementSecuritySeeder>().EnsureSeededAsync();
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task AssertPermissionDeniedAsync(
        HttpResponseMessage response,
        string resourceKind,
        string resourceCode,
        string? actionCode = null)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("permission_denied");
        body.Should().Contain(resourceKind);
        body.Should().Contain(resourceCode);
        if (actionCode is not null)
            body.Should().Contain(actionCode);
    }

    private sealed record MenuPermissionMapping(
        NgbPermissionKey Permission,
        string ExpectedKind,
        string ExpectedCode,
        string ExpectedRoute);

    private static async Task<IReadOnlyList<AuditEvent>> ReadAuditEventsAsync(
        PmApiFactory factory,
        AuditEntityKind entityKind,
        Guid entityId,
        string actionCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        return await reader.QueryAsync(new AuditLogQuery(
            EntityKind: entityKind,
            EntityId: entityId,
            ActionCode: actionCode,
            Limit: 50,
            Offset: 0));
    }

    private static AuditFieldChange GetChange(AuditEvent auditEvent, string fieldPath)
        => auditEvent.Changes.Should().ContainSingle(change => change.FieldPath == fieldPath).Subject;

    private static string[] ReadObjectArrayProperty(string? json, string propertyName)
    {
        json.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(json!);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        return doc.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty(propertyName).GetString())
            .Where(static value => value is not null)
            .Select(static value => value!)
            .ToArray();
    }

    private static string? ReadObjectProperty(string? json, string propertyName)
    {
        json.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(json!);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        return doc.RootElement.GetProperty(propertyName).GetString();
    }
}

internal static class SecurityHttpResponseAssertions
{
    public static async Task ShouldHaveStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
            return;

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, "response body was: {0}", body);
    }
}
