using Microsoft.Extensions.Caching.Memory;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Search;
using NGB.Contracts.Services;
using NGB.Tools.Exceptions;

namespace NGB.CRM.Api.Services;

public sealed class CrmCommandPaletteSearchService(
    IDocumentService documents,
    ICatalogService catalogs,
    IReportDefinitionProvider reports,
    IMemoryCache cache,
    ILogger<CrmCommandPaletteSearchService> logger)
{
    private const string DocumentsCode = "documents";
    private const string CatalogsCode = "catalogs";
    private const string ReportsCode = "reports";
    private const int MaxQueryLength = 256;

    private static readonly TimeSpan MetadataCacheTtl = TimeSpan.FromMinutes(10);

    public async Task<CommandPaletteSearchResponseDto> SearchAsync(
        CommandPaletteSearchRequestDto request,
        CancellationToken ct)
    {
        var query = NormalizeQuery(request.Query);
        if (query is null)
            return new CommandPaletteSearchResponseDto([]);

        var scope = NormalizeScope(request.Scope);
        var limit = Math.Min(request.Limit <= 0 ? 20 : request.Limit, 30);
        var groups = new List<CommandPaletteGroupDto>(capacity: 3);
        var reportsTask = scope is null or ReportsCode
            ? SearchReportsAsync(query, limit, ct)
            : null;

        if (scope is null or DocumentsCode)
            await AddGroupAsync(groups, DocumentsCode, () => SearchDocumentsAsync(query, limit, request.Context, ct), ct);

        if (scope is null or CatalogsCode)
            await AddGroupAsync(groups, CatalogsCode, () => SearchCatalogsAsync(query, limit, request.Context, ct), ct);

        if (reportsTask is not null)
            await AddGroupAsync(groups, ReportsCode, () => reportsTask, ct);

        return new CommandPaletteSearchResponseDto(groups);
    }

    private static string? NormalizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        if (query.Length > MaxQueryLength)
            throw new NgbArgumentOutOfRangeException(nameof(query), query.Length, $"Search text can contain up to {MaxQueryLength} characters.");

        return query.Trim();
    }

    private async Task AddGroupAsync(
        ICollection<CommandPaletteGroupDto> groups,
        string providerCode,
        Func<Task<CommandPaletteGroupDto?>> action,
        CancellationToken ct)
    {
        try
        {
            var group = await action();
            if (group is not null)
                groups.Add(group);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CRM command palette provider {ProviderCode} failed.", providerCode);
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

        var descriptorByCode = descriptors
            .ToDictionary(static x => x.Code, StringComparer.OrdinalIgnoreCase);

        var hits = await documents.LookupAcrossTypesAsync(
            descriptors.Select(static x => x.Code).ToArray(),
            query,
            Math.Clamp(Math.Min(limit, 6), 3, 6),
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

        return items.Length == 0 ? null : new CommandPaletteGroupDto(DocumentsCode, "Documents", items);
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

        var descriptorByCode = descriptors
            .ToDictionary(static x => x.Code, StringComparer.OrdinalIgnoreCase);

        var hits = await catalogs.LookupAcrossTypesAsync(
            descriptors.Select(static x => x.Code).ToArray(),
            query,
            Math.Clamp(Math.Min(limit, 6), 3, 6),
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

        return items.Length == 0 ? null : new CommandPaletteGroupDto(CatalogsCode, "Catalogs", items);
    }

    private async Task<CommandPaletteGroupDto?> SearchReportsAsync(string query, int limit, CancellationToken ct)
    {
        var definitions = await GetReportDefinitionsAsync(ct);
        var items = definitions
            .Select(definition => CreateReportItem(query, definition))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();

        return items.Length == 0 ? null : new CommandPaletteGroupDto(ReportsCode, "Reports", items);
    }

    private static CommandPaletteResultItemDto? CreateDocumentItem(
        string query,
        SearchableDescriptor descriptor,
        DocumentLookupDto document,
        CommandPaletteSearchContextDto? context)
    {
        var number = document.Number?.Trim();
        var display = document.Display?.Trim();
        var title = $"{descriptor.Label} {ResolveDocumentTitleValue(number, display, document.Id)}";

        var score = Score(query, number, display, descriptor.Code, descriptor.Aliases);
        if (score <= 0m)
            return null;

        if (string.Equals(context?.DocumentType, descriptor.Code, StringComparison.OrdinalIgnoreCase))
            score += 0.03m;

        return new CommandPaletteResultItemDto(
            Key: $"document:{descriptor.Code}:{document.Id}",
            Kind: "document",
            Title: title,
            Subtitle: string.Join(" · ", new[]
                {
                    display, GetDocumentStatusLabel(document.Status)
                }
                .Where(static x => !string.IsNullOrWhiteSpace(x))),
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
        var score = Score(query, display, descriptor.Label, descriptor.Code, descriptor.Aliases);
        if (score <= 0m)
            return null;

        if (string.Equals(context?.CatalogType, descriptor.Code, StringComparison.OrdinalIgnoreCase))
            score += 0.03m;

        return new CommandPaletteResultItemDto(
            Key: $"catalog:{descriptor.Code}:{catalog.Id}",
            Kind: "catalog",
            Title: title,
            Subtitle: catalog.IsMarkedForDeletion ? $"{descriptor.Label} · Marked for deletion" : descriptor.Label,
            Icon: descriptor.Icon,
            Badge: "Catalog",
            Route: $"/catalogs/{descriptor.Code}/{catalog.Id}",
            CommandCode: null,
            Status: catalog.IsMarkedForDeletion ? "marked-for-deletion" : null,
            OpenInNewTabSupported: true,
            Score: decimal.Round(score, 4));
    }

    private static CommandPaletteResultItemDto? CreateReportItem(string query, ReportDefinitionDto definition)
    {
        var group = definition.Group?.Trim();
        var description = definition.Description?.Trim();
        var score = Score(query, definition.Name, definition.ReportCode, group, description);
        if (score <= 0m)
            return null;

        var subtitle = string.Join(" · ", new[] { group, description }.Where(static x => !string.IsNullOrWhiteSpace(x)));

        return new CommandPaletteResultItemDto(
            Key: $"report:{definition.ReportCode}",
            Kind: "report",
            Title: definition.Name,
            Subtitle: subtitle.Length > 0 ? subtitle : "Report",
            Icon: ResolveReportIcon(definition.ReportCode),
            Badge: "Report",
            Route: $"/reports/{definition.ReportCode}",
            CommandCode: null,
            Status: null,
            OpenInNewTabSupported: true,
            Score: decimal.Round(score, 4));
    }

    private async Task<IReadOnlyList<SearchableDescriptor>> GetDocumentDescriptorsAsync(CancellationToken ct)
        => (await cache.GetOrCreateAsync(
               "crm-command-palette:documents",
               async entry =>
               {
                   entry.AbsoluteExpirationRelativeToNow = MetadataCacheTtl;
                   var metadata = await documents.GetAllMetadataAsync(ct);
                   return metadata
                       .Where(static item => item.DocumentType.StartsWith("crm.", StringComparison.OrdinalIgnoreCase))
                       .Select(item => new SearchableDescriptor(
                           item.DocumentType,
                           item.DisplayName,
                           ResolveItemIcon(item.Icon, "file-text"),
                           ResolveAliases(item.DocumentType, item.DisplayName)))
                       .ToArray();
               }))!;

    private async Task<IReadOnlyList<SearchableDescriptor>> GetCatalogDescriptorsAsync(CancellationToken ct)
        => (await cache.GetOrCreateAsync(
               "crm-command-palette:catalogs",
               async entry =>
               {
                   entry.AbsoluteExpirationRelativeToNow = MetadataCacheTtl;
                   var metadata = await catalogs.GetAllMetadataAsync(ct);
                   return metadata
                       .Where(static item => item.CatalogType.StartsWith("crm.", StringComparison.OrdinalIgnoreCase))
                       .Select(item => new SearchableDescriptor(
                           item.CatalogType,
                           item.DisplayName,
                           ResolveItemIcon(item.Icon, "grid"),
                           ResolveAliases(item.CatalogType, item.DisplayName)))
                       .ToArray();
               }))!;

    private async Task<IReadOnlyList<ReportDefinitionDto>> GetReportDefinitionsAsync(CancellationToken ct)
        => (await cache.GetOrCreateAsync(
               "crm-command-palette:reports",
               async entry =>
               {
                   entry.AbsoluteExpirationRelativeToNow = MetadataCacheTtl;
                   var definitions = await reports.GetAllDefinitionsAsync(ct);
                   return definitions
                       .Where(static definition => definition.ReportCode.StartsWith("crm.", StringComparison.OrdinalIgnoreCase))
                       .ToArray();
               }))!;

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
            _ when string.Equals(code, CrmCodes.Account, StringComparison.OrdinalIgnoreCase)
                => ["account", "company", "customer", label],
            _ when string.Equals(code, CrmCodes.Contact, StringComparison.OrdinalIgnoreCase)
                => ["contact", "person", "stakeholder", label],
            _ when string.Equals(code, CrmCodes.Product, StringComparison.OrdinalIgnoreCase)
                => ["product", "sku", "quote line", label],
            _ when string.Equals(code, CrmCodes.OpportunityStage, StringComparison.OrdinalIgnoreCase)
                => ["stage", "pipeline stage", label],
            _ when string.Equals(code, CrmCodes.LeadIntake, StringComparison.OrdinalIgnoreCase)
                => ["lead", "intake", "prospect", label],
            _ when string.Equals(code, CrmCodes.LeadConversion, StringComparison.OrdinalIgnoreCase)
                => ["opportunity", "deal", "conversion", label],
            _ when string.Equals(code, CrmCodes.Quote, StringComparison.OrdinalIgnoreCase)
                => ["quote", "proposal", label],
            _ when string.Equals(code, CrmCodes.ActivityLog, StringComparison.OrdinalIgnoreCase)
                => ["activity", "call", "meeting", "task", label],
            _ => [label, code]
        };

    private static string ResolveItemIcon(string? icon, string fallback)
        => string.IsNullOrWhiteSpace(icon) ? fallback : icon.Trim();

    private static string ResolveReportIcon(string reportCode)
        => reportCode switch
        {
            CrmCodes.SalesPipelineReport => "bar-chart",
            CrmCodes.OpportunityHistoryReport => "history",
            CrmCodes.LeadConversionFunnelReport => "filter",
            CrmCodes.ActivitySummaryReport => "calendar-check",
            CrmCodes.QuoteRegisterReport => "file-text",
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

    private static string ResolveDocumentTitleValue(string? number, string? display, Guid id)
    {
        if (!string.IsNullOrEmpty(number))
            return number;

        if (!string.IsNullOrEmpty(display))
            return display;

        return id.ToString();
    }

    private static decimal Score(string query, params string?[] candidates)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0) return 0m;

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

        return normalizedCandidate.Contains(normalizedQuery, StringComparison.Ordinal)
            ? 0.78m
            : 0m;
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
