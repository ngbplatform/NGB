using NGB.Api.Models;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;

namespace NGB.AgencyBilling.Api.Services;

internal sealed class AgencyBillingMainMenuContributor(
    ICatalogTypeRegistry catalogs,
    IDocumentTypeRegistry documents,
    IReportDefinitionProvider reports,
    ExternalLinksSettings externalLinks)
    : IMainMenuContributor
{
    public async Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        var availableCatalogs = catalogs
            .All()
            .Select(static x => x.CatalogCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var availableDocuments = documents
            .GetAll()
            .Select(static x => x.TypeCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var availableReports = (await reports.GetAllDefinitionsAsync(ct))
            .Select(static x => x.ReportCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var groups = new List<MainMenuGroupDto>();

        var dashboardItems = new[]
        {
            CreatePageItem(AgencyBillingCodes.DashboardOverviewReport, "Dashboard", "/home", "home", 10)
        };

        AddGroupIfAny(groups, "Dashboard", "home", 5, dashboardItems);
        AddGroupIfAny(
            groups,
            "Portfolio",
            "grid",
            10,
            CreateCatalogItem(availableCatalogs, AgencyBillingCodes.Client, "Clients", "building-2", 10),
            CreateCatalogItem(availableCatalogs, AgencyBillingCodes.TeamMember, "Team Members", "users", 20),
            CreateCatalogItem(availableCatalogs, AgencyBillingCodes.Project, "Projects", "grid", 30),
            CreateCatalogItem(availableCatalogs, AgencyBillingCodes.RateCard, "Rate Cards", "calculator", 40),
            CreateCatalogItem(availableCatalogs, AgencyBillingCodes.ServiceItem, "Service Items", "tag", 50));
        AddGroupIfAny(
            groups,
            "Operations",
            "clipboard-list",
            20,
            CreateDocumentItem(availableDocuments, AgencyBillingCodes.ClientContract, "Client Contracts", "file-text", 10),
            CreateDocumentItem(availableDocuments, AgencyBillingCodes.Timesheet, "Timesheets", "file-text", 20));
        AddGroupIfAny(
            groups,
            "Billing",
            "coins",
            30,
            CreateDocumentItem(availableDocuments, AgencyBillingCodes.SalesInvoice, "Sales Invoices", "file-text", 10),
            CreateDocumentItem(availableDocuments, AgencyBillingCodes.CustomerPayment, "Customer Payments", "file-text", 20));
        AddGroupIfAny(
            groups,
            "Reports",
            "bar-chart",
            40,
            CreatePageItemIfPresent(availableReports, AgencyBillingCodes.UnbilledTimeReport, "Unbilled Time", $"/reports/{AgencyBillingCodes.UnbilledTimeReport}", "bar-chart", 10),
            CreatePageItemIfPresent(availableReports, AgencyBillingCodes.ProjectProfitabilityReport, "Project Profitability", $"/reports/{AgencyBillingCodes.ProjectProfitabilityReport}", "bar-chart", 20),
            CreatePageItemIfPresent(availableReports, AgencyBillingCodes.InvoiceRegisterReport, "Invoice Register", $"/reports/{AgencyBillingCodes.InvoiceRegisterReport}", "bar-chart", 30),
            CreatePageItemIfPresent(availableReports, AgencyBillingCodes.ArAgingReport, "AR Aging", $"/reports/{AgencyBillingCodes.ArAgingReport}", "bar-chart", 40),
            CreatePageItemIfPresent(availableReports, AgencyBillingCodes.TeamUtilizationReport, "Team Utilization", $"/reports/{AgencyBillingCodes.TeamUtilizationReport}", "bar-chart", 50));
        AddGroupIfAny(
            groups,
            "Setup & Controls",
            "settings",
            70,
            CreateCatalogItem(availableCatalogs, AgencyBillingCodes.PaymentTerms, "Payment Terms", "list", 10),
            CreateCatalogItem(availableCatalogs, AgencyBillingCodes.AccountingPolicy, "Accounting Policy", "settings", 20),
            CreateExternalItem(externalLinks.HealthUiUrl, AgencyBillingCodes.Watchdog, "Health", "heart-pulse", 90),
            CreateExternalItem(externalLinks.BackgroundJobsUiUrl, AgencyBillingCodes.BackgroundJobs, "Background Jobs", "cogs", 100));

        return groups;
    }

    private static void AddGroupIfAny(
        ICollection<MainMenuGroupDto> groups,
        string label,
        string icon,
        int ordinal,
        params MainMenuItemDto?[] items)
    {
        var visibleItems = items
            .Where(static item => item is not null)
            .Cast<MainMenuItemDto>()
            .OrderBy(static item => item.Ordinal)
            .ToArray();

        if (visibleItems.Length == 0)
            return;

        groups.Add(new MainMenuGroupDto(label, visibleItems, ordinal, icon));
    }

    private static MainMenuItemDto? CreatePageItemIfPresent(
        IReadOnlySet<string> availableReports,
        string reportCode,
        string label,
        string route,
        string icon,
        int ordinal)
        => availableReports.Contains(reportCode)
            ? new MainMenuItemDto("page", reportCode, label, route, icon, ordinal)
            : null;

    private static MainMenuItemDto CreatePageItem(string code, string label, string route, string icon, int ordinal)
        => new("page", code, label, route, icon, ordinal);

    private static MainMenuItemDto? CreateExternalItem(string? url, string code, string label, string icon, int ordinal)
        => string.IsNullOrWhiteSpace(url)
            ? null
            : new MainMenuItemDto("external", code, label, url, icon, ordinal);

    private static MainMenuItemDto? CreateCatalogItem(
        IReadOnlySet<string> availableCatalogs,
        string catalogCode,
        string label,
        string icon,
        int ordinal)
        => availableCatalogs.Contains(catalogCode)
            ? new MainMenuItemDto("catalog", catalogCode, label, $"/catalogs/{catalogCode}", icon, ordinal)
            : null;

    private static MainMenuItemDto? CreateDocumentItem(
        IReadOnlySet<string> availableDocuments,
        string documentType,
        string label,
        string icon,
        int ordinal)
        => availableDocuments.Contains(documentType)
            ? new MainMenuItemDto("document", documentType, label, $"/documents/{documentType}", icon, ordinal)
            : null;
}
