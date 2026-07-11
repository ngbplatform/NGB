using NGB.Api.Models;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Admin;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;

namespace NGB.CRM.Api.Services;

internal sealed class CrmMainMenuContributor(
    ICatalogTypeRegistry catalogs,
    IDocumentTypeRegistry documents,
    ExternalLinksSettings externalLinks)
    : IMainMenuContributor
{
    public Task<IReadOnlyList<MainMenuGroupDto>> ContributeAsync(CancellationToken ct)
    {
        var availableCatalogs = catalogs
            .All()
            .Select(static x => x.CatalogCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var availableDocuments = documents
            .GetAll()
            .Select(static x => x.TypeCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<MainMenuGroupDto> groups =
        [
            CreateGroup(
                "Dashboard",
                "home",
                5,
                CreatePageItem(CrmCodes.Dashboard, "Dashboard", "/home", "home", 10)),
            CreateGroup(
                "Pipeline",
                "clipboard-list",
                10,
                CreateDocumentItem(availableDocuments, CrmCodes.LeadIntake, "Leads", "file-text", 10),
                CreateDocumentItem(availableDocuments, CrmCodes.LeadQualification, "Lead Qualifications", "file-text", 20),
                CreateDocumentItem(availableDocuments, CrmCodes.LeadConversion, "Lead Conversions", "file-text", 30),
                CreateDocumentItem(availableDocuments, CrmCodes.OpportunityUpdate, "Opportunity Updates", "file-text", 40),
                CreatePageItem(CrmCodes.SalesPipelineReport, "Sales Pipeline", $"/reports/{CrmCodes.SalesPipelineReport}", "bar-chart", 50)),
            CreateGroup(
                "Customers",
                "users",
                20,
                CreateCatalogItem(availableCatalogs, CrmCodes.Account, "Accounts", "building-2", 10),
                CreateCatalogItem(availableCatalogs, CrmCodes.Contact, "Contacts", "user", 20),
                CreateDocumentItem(availableDocuments, CrmCodes.ActivityLog, "Activities", "file-text", 30),
                CreatePageItem(CrmCodes.ActivitySummaryReport, "Activity Summary", $"/reports/{CrmCodes.ActivitySummaryReport}", "bar-chart", 40)),
            CreateGroup(
                "Quotes",
                "file-text",
                30,
                CreateCatalogItem(availableCatalogs, CrmCodes.Product, "Products", "tag", 10),
                CreateDocumentItem(availableDocuments, CrmCodes.Quote, "Quotes", "file-text", 20),
                CreatePageItem(CrmCodes.QuoteRegisterReport, "Quote Register", $"/reports/{CrmCodes.QuoteRegisterReport}", "bar-chart", 30)),
            CreateGroup(
                "Insights",
                "bar-chart",
                40,
                CreatePageItem(CrmCodes.OpportunityHistoryReport, "Opportunity History", $"/reports/{CrmCodes.OpportunityHistoryReport}", "bar-chart", 10),
                CreatePageItem(CrmCodes.LeadConversionFunnelReport, "Lead Conversion Funnel", $"/reports/{CrmCodes.LeadConversionFunnelReport}", "bar-chart", 20)),
            CreateGroup(
                "Setup & Controls",
                "settings",
                70,
                CreateCatalogItem(availableCatalogs, CrmCodes.OpportunityStage, "Opportunity Stages", "list", 10),
                CreatePageItem("system.users", "Users", "/admin/security/users", "users", 55),
                CreatePageItem("system.roles", "Roles & Permissions", "/admin/security/roles", "shield", 56),
                CreateExternalItem(externalLinks.HealthUiUrl, CrmCodes.Watchdog, "Health", "heart-pulse", 90),
                CreateExternalItem(externalLinks.BackgroundJobsUiUrl, CrmCodes.BackgroundJobs, "Background Jobs", "cogs", 100))
        ];

        return Task.FromResult(groups);
    }

    private static MainMenuGroupDto CreateGroup(string label, string icon, int ordinal, params MainMenuItemDto?[] items)
    {
        var visibleItems = items
            .Where(static item => item is not null)
            .Cast<MainMenuItemDto>()
            .OrderBy(static item => item.Ordinal)
            .ToArray();

        return new MainMenuGroupDto(label, visibleItems, ordinal, icon);
    }

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
