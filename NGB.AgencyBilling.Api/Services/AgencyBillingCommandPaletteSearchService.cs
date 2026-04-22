using Microsoft.Extensions.Caching.Memory;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Search;
using NGB.Contracts.Services;

namespace NGB.AgencyBilling.Api.Services;

public sealed class AgencyBillingCommandPaletteSearchService(
    IDocumentService documents,
    ICatalogService catalogs,
    IReportDefinitionProvider reports,
    IMemoryCache cache,
    ILogger<AgencyBillingCommandPaletteSearchService> logger)
{
    private const string DocumentsCode = "documents";
    private const string CatalogsCode = "catalogs";
    private const string ReportsCode = "reports";

    private static readonly TimeSpan MetadataCacheTtl = TimeSpan.FromMinutes(10);

    public async Task<CommandPaletteSearchResponseDto> SearchAsync(
        CommandPaletteSearchRequestDto request,
        CancellationToken ct)
    {
        var query = (request.Query ?? string.Empty).Trim();
        if (query.Length == 0)
            return new CommandPaletteSearchResponseDto([]);

        var scope = NormalizeScope(request.Scope);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 30);
        var groups = new List<CommandPaletteGroupDto>(capacity: 3);

        if (scope is null or DocumentsCode)
        {
            var documentsGroup = await SafeGroupAsync(DocumentsCode, () => SearchDocumentsAsync(query, limit, request.Context, ct), ct);
            if (documentsGroup is not null && documentsGroup.Items.Count > 0)
                groups.Add(documentsGroup);
        }

        if (scope is null or CatalogsCode)
        {
            var catalogsGroup = await SafeGroupAsync(CatalogsCode, () => SearchCatalogsAsync(query, limit, request.Context, ct), ct);
            if (catalogsGroup is not null && catalogsGroup.Items.Count > 0)
                groups.Add(catalogsGroup);
        }

        if (scope is null or ReportsCode)
        {
            var reportsGroup = await SafeGroupAsync(ReportsCode, () => SearchReportsAsync(query, limit, request.Context, ct), ct);
            if (reportsGroup is not null && reportsGroup.Items.Count > 0)
                groups.Add(reportsGroup);
        }

        return new CommandPaletteSearchResponseDto(groups
            .OrderBy(static group => GroupOrder(group.Code))
            .ToArray());
    }

    private async Task<CommandPaletteGroupDto?> SafeGroupAsync(
        string providerCode,
        Func<Task<CommandPaletteGroupDto?>> action,
        CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agency Billing command palette provider {ProviderCode} failed.", providerCode);
            return null;
        }
    }

    private async Task<CommandPaletteGroupDto?> SearchDocumentsAsync(
        string query,
        int limit,
        CommandPaletteSearchContextDto? context,
        CancellationToken ct)
    {
        var descriptors = await GetDocumentDescriptorsAsync(ct);
        if (descriptors.Count == 0)
            return null;

        var perTypeLimit = Math.Clamp(Math.Min(limit, 6), 3, 6);
        var descriptorByCode = descriptors.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var hits = await documents.LookupAcrossTypesAsync(
            descriptors.Select(x => x.Code).ToArray(),
            query,
            perTypeLimit,
            activeOnly: false,
            ct);

        var items = hits
            .Select(hit => descriptorByCode.TryGetValue(hit.DocumentType, out var descriptor)
                ? CreateDocumentItem(query, descriptor, hit, context)
                : null)
            .Where(static item => item is not null)
            .Select(static item => item!)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        return items.Length == 0
            ? null
            : new CommandPaletteGroupDto(DocumentsCode, "Documents", items);
    }

    private async Task<CommandPaletteGroupDto?> SearchCatalogsAsync(
        string query,
        int limit,
        CommandPaletteSearchContextDto? context,
        CancellationToken ct)
    {
        var descriptors = await GetCatalogDescriptorsAsync(ct);
        if (descriptors.Count == 0)
            return null;

        var perTypeLimit = Math.Clamp(Math.Min(limit, 6), 3, 6);
        var descriptorByCode = descriptors.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var hits = await catalogs.LookupAcrossTypesAsync(
            descriptors.Select(x => x.Code).ToArray(),
            query,
            perTypeLimit,
            activeOnly: true,
            ct);

        var items = hits
            .Select(hit => descriptorByCode.TryGetValue(hit.CatalogType, out var descriptor)
                ? CreateCatalogItem(query, descriptor, hit, context)
                : null)
            .Where(static item => item is not null)
            .Select(static item => item!)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        return items.Length == 0
            ? null
            : new CommandPaletteGroupDto(CatalogsCode, "Catalogs", items);
    }

    private async Task<CommandPaletteGroupDto?> SearchReportsAsync(
        string query,
        int limit,
        CommandPaletteSearchContextDto? context,
        CancellationToken ct)
    {
        var definitions = await GetReportDefinitionsAsync(ct);
        var items = definitions
            .Select(definition => CreateReportItem(query, definition, context))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        return items.Length == 0
            ? null
            : new CommandPaletteGroupDto(ReportsCode, "Reports", items);
    }

    private static CommandPaletteResultItemDto? CreateDocumentItem(
        string query,
        SearchableDescriptor descriptor,
        DocumentLookupDto document,
        CommandPaletteSearchContextDto? context)
    {
        var number = document.Number?.Trim();
        var display = document.Display?.Trim();
        var title = number?.Length > 0
            ? $"{descriptor.Label} {number}"
            : $"{descriptor.Label} {display ?? document.Id.ToString()}";

        var subtitleParts = new List<string>(capacity: 3);
        if (display?.Length > 0 && !string.Equals(display, number, StringComparison.OrdinalIgnoreCase))
            subtitleParts.Add(display);

        subtitleParts.Add(GetDocumentStatusLabel(document.Status));

        var score = Score(query, number, display, descriptor.Code, descriptor.Aliases);
        if (score <= 0m)
            return null;

        if (string.Equals(context?.DocumentType, descriptor.Code, StringComparison.OrdinalIgnoreCase))
            score += 0.03m;

        return new CommandPaletteResultItemDto(
            Key: $"document:{descriptor.Code}:{document.Id}",
            Kind: "document",
            Title: title,
            Subtitle: string.Join(" · ", subtitleParts),
            Icon: descriptor.Icon,
            Badge: "Document",
            Route: $"/documents/{descriptor.Code}/{document.Id}",
            CommandCode: null,
            Status: document.Status.ToString().ToLowerInvariant(),
            OpenInNewTabSupported: true,
            Score: decimal.Round(score, 4));
    }

    private static CommandPaletteResultItemDto? CreateCatalogItem(
        string query,
        SearchableDescriptor descriptor,
        CatalogLookupDto catalog,
        CommandPaletteSearchContextDto? context)
    {
        var display = catalog.Display?.Trim();
        var title = display?.Length > 0 ? display : $"{descriptor.Label} {catalog.Id}";
        var subtitleParts = new List<string>(capacity: 2) { descriptor.Label };
        if (catalog.IsMarkedForDeletion)
            subtitleParts.Add("Marked for deletion");

        var score = Score(query, display, descriptor.Label, descriptor.Code, string.Join(' ', descriptor.Aliases));
        if (score <= 0m)
            return null;

        if (string.Equals(context?.CatalogType, descriptor.Code, StringComparison.OrdinalIgnoreCase))
            score += 0.03m;

        return new CommandPaletteResultItemDto(
            Key: $"catalog:{descriptor.Code}:{catalog.Id}",
            Kind: "catalog",
            Title: title,
            Subtitle: string.Join(" · ", subtitleParts),
            Icon: descriptor.Icon,
            Badge: "Catalog",
            Route: $"/catalogs/{descriptor.Code}/{catalog.Id}",
            CommandCode: null,
            Status: catalog.IsMarkedForDeletion ? "marked-for-deletion" : null,
            OpenInNewTabSupported: true,
            Score: decimal.Round(score, 4));
    }

    private static CommandPaletteResultItemDto? CreateReportItem(
        string query,
        ReportDefinitionDto definition,
        CommandPaletteSearchContextDto? context)
    {
        var group = definition.Group?.Trim();
        var description = definition.Description?.Trim();
        var score = Score(query, definition.Name, definition.ReportCode, group, description);
        if (score <= 0m)
            return null;

        if (string.Equals(context?.EntityType, "report", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context?.EntityId?.ToString(), definition.ReportCode, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.02m;
        }

        var subtitleParts = new List<string>(capacity: 2);
        if (group?.Length > 0)
            subtitleParts.Add(group);
        if (description?.Length > 0)
            subtitleParts.Add(description);

        return new CommandPaletteResultItemDto(
            Key: $"report:{definition.ReportCode}",
            Kind: "report",
            Title: definition.Name,
            Subtitle: subtitleParts.Count > 0 ? string.Join(" · ", subtitleParts) : "Report",
            Icon: ResolveReportIcon(definition.ReportCode),
            Badge: "Report",
            Route: $"/reports/{definition.ReportCode}",
            CommandCode: null,
            Status: null,
            OpenInNewTabSupported: true,
            Score: decimal.Round(score, 4));
    }

    private async Task<IReadOnlyList<SearchableDescriptor>> GetDocumentDescriptorsAsync(CancellationToken ct)
        => await cache.GetOrCreateAsync(
               "agency-billing-command-palette:documents",
               async entry =>
               {
                   entry.AbsoluteExpirationRelativeToNow = MetadataCacheTtl;
                   var metadata = await documents.GetAllMetadataAsync(ct);
                   return metadata
                       .Where(static item =>
                           item.DocumentType.StartsWith("ab.", StringComparison.OrdinalIgnoreCase)
                           || item.DocumentType.StartsWith("accounting.", StringComparison.OrdinalIgnoreCase))
                       .Select(item => new SearchableDescriptor(
                           item.DocumentType,
                           item.DisplayName,
                           ResolveItemIcon(item.Icon, "file-text"),
                           ResolveAliases(item.DocumentType, item.DisplayName)))
                       .ToArray();
               })
           ?? [];

    private async Task<IReadOnlyList<SearchableDescriptor>> GetCatalogDescriptorsAsync(CancellationToken ct)
        => await cache.GetOrCreateAsync(
               "agency-billing-command-palette:catalogs",
               async entry =>
               {
                   entry.AbsoluteExpirationRelativeToNow = MetadataCacheTtl;
                   var metadata = await catalogs.GetAllMetadataAsync(ct);
                   return metadata
                       .Where(static item => item.CatalogType.StartsWith("ab.", StringComparison.OrdinalIgnoreCase))
                       .Select(item => new SearchableDescriptor(
                           item.CatalogType,
                           item.DisplayName,
                           ResolveItemIcon(item.Icon, "grid"),
                           ResolveAliases(item.CatalogType, item.DisplayName)))
                       .ToArray();
               })
           ?? [];

    private async Task<IReadOnlyList<ReportDefinitionDto>> GetReportDefinitionsAsync(CancellationToken ct)
        => await cache.GetOrCreateAsync(
               "agency-billing-command-palette:reports",
               async entry =>
               {
                   entry.AbsoluteExpirationRelativeToNow = MetadataCacheTtl;
                   var definitions = await reports.GetAllDefinitionsAsync(ct);
                   return definitions
                       .Where(static definition =>
                           definition.ReportCode.StartsWith("ab.", StringComparison.OrdinalIgnoreCase)
                           || definition.ReportCode.StartsWith("accounting.", StringComparison.OrdinalIgnoreCase))
                       .Where(static definition =>
                           !string.Equals(definition.ReportCode, "accounting.posting_log", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(definition.ReportCode, "accounting.consistency", StringComparison.OrdinalIgnoreCase))
                       .ToArray();
               })
           ?? [];

    private static string? NormalizeScope(string? scope)
        => (scope ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" => null,
            ":" or "document" or DocumentsCode => DocumentsCode,
            "@" or "catalog" or CatalogsCode => CatalogsCode,
            "#" or "report" or ReportsCode => ReportsCode,
            "/" or "page" or "pages" => "pages",
            ">" or "command" or "commands" => "commands",
            var value => value,
        };

    private static string[] ResolveAliases(string code, string label)
        => code switch
        {
            _ when string.Equals(code, AgencyBillingCodes.Client, StringComparison.OrdinalIgnoreCase)
                => ["client", "customer", "account", label],
            _ when string.Equals(code, AgencyBillingCodes.TeamMember, StringComparison.OrdinalIgnoreCase)
                => ["team", "member", "resource", "employee", "contractor", label],
            _ when string.Equals(code, AgencyBillingCodes.Project, StringComparison.OrdinalIgnoreCase)
                => ["project", "engagement", label],
            _ when string.Equals(code, AgencyBillingCodes.RateCard, StringComparison.OrdinalIgnoreCase)
                => ["rate", "rate card", "pricing", label],
            _ when string.Equals(code, AgencyBillingCodes.ServiceItem, StringComparison.OrdinalIgnoreCase)
                => ["service", "service item", "practice", label],
            _ when string.Equals(code, AgencyBillingCodes.ClientContract, StringComparison.OrdinalIgnoreCase)
                => ["contract", "client contract", "billing terms", label],
            _ when string.Equals(code, AgencyBillingCodes.Timesheet, StringComparison.OrdinalIgnoreCase)
                => ["timesheet", "time", "hours", "worklog", label],
            _ when string.Equals(code, AgencyBillingCodes.SalesInvoice, StringComparison.OrdinalIgnoreCase)
                => ["invoice", "sales invoice", "billing", label],
            _ when string.Equals(code, AgencyBillingCodes.CustomerPayment, StringComparison.OrdinalIgnoreCase)
                => ["payment", "cash receipt", "collection", label],
            _ => [label, code]
        };

    private static string ResolveItemIcon(string? icon, string fallback)
        => string.IsNullOrWhiteSpace(icon) ? fallback : icon.Trim();

    private static string ResolveReportIcon(string reportCode)
        => reportCode switch
        {
            AgencyBillingCodes.DashboardOverviewReport => "home",
            AgencyBillingCodes.UnbilledTimeReport => "calendar-check",
            AgencyBillingCodes.ProjectProfitabilityReport => "bar-chart",
            AgencyBillingCodes.InvoiceRegisterReport => "receipt",
            AgencyBillingCodes.ArAgingReport => "book-open",
            AgencyBillingCodes.TeamUtilizationReport => "users",
            _ when reportCode.StartsWith("accounting.", StringComparison.OrdinalIgnoreCase) => "receipt",
            _ => "bar-chart"
        };

    private static string GetDocumentStatusLabel(DocumentStatus status)
        => status switch
        {
            DocumentStatus.Draft => "Draft",
            DocumentStatus.Posted => "Posted",
            DocumentStatus.MarkedForDeletion => "Marked for deletion",
            _ => status.ToString()
        };

    private static int GroupOrder(string code)
        => code switch
        {
            "actions" => 0,
            "go-to" => 1,
            DocumentsCode => 2,
            CatalogsCode => 3,
            ReportsCode => 4,
            "recent" => 5,
            _ => 99
        };

    private static decimal Score(string query, params string?[] candidates)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
            return 0m;

        var best = 0m;
        foreach (var candidate in candidates)
        {
            best = Math.Max(best, ScoreCandidate(normalizedQuery, candidate));
        }

        return best;
    }

    private static decimal Score(
        string query,
        string? primary,
        string? secondary,
        string? tertiary,
        IReadOnlyList<string> aliases)
    {
        var best = Score(query, primary, secondary, tertiary);
        foreach (var alias in aliases)
        {
            best = Math.Max(best, Score(query, alias));
        }

        return best;
    }

    private static decimal ScoreCandidate(string normalizedQuery, string? candidate)
    {
        var normalizedCandidate = Normalize(candidate);
        if (normalizedCandidate.Length == 0)
            return 0m;

        if (normalizedCandidate.Equals(normalizedQuery, StringComparison.Ordinal))
            return 1.0m;
        if (normalizedCandidate.StartsWith(normalizedQuery, StringComparison.Ordinal))
            return 0.92m;
        if (normalizedCandidate.Contains(normalizedQuery, StringComparison.Ordinal))
            return 0.78m;

        foreach (var token in normalizedCandidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith(normalizedQuery, StringComparison.Ordinal))
                return 0.72m;
        }

        return 0m;
    }

    private static string Normalize(string? value)
        => string.Join(
            ' ',
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split([' ', '-', '_', '.', '/', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed record SearchableDescriptor(
        string Code,
        string Label,
        string Icon,
        IReadOnlyList<string> Aliases);
}
