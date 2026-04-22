using System.Text;
using Microsoft.Extensions.Caching.Memory;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Search;
using NGB.Contracts.Services;

namespace NGB.PropertyManagement.Api.Services;

internal sealed class CommandPaletteSearchService(
    IDocumentService documents,
    ICatalogService catalogs,
    IReportDefinitionProvider reports,
    IMemoryCache cache,
    ILogger<CommandPaletteSearchService> logger)
    : ICommandPaletteSearchService
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
            logger.LogWarning(ex, "Command palette provider {ProviderCode} failed.", providerCode);
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
            .Select(document => descriptorByCode.TryGetValue(document.DocumentType, out var descriptor)
                ? CreateDocumentItem(query, descriptor, document, context)
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
            .Select(catalog => descriptorByCode.TryGetValue(catalog.CatalogType, out var descriptor)
                ? CreateCatalogItem(query, descriptor, catalog, context)
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

    private CommandPaletteResultItemDto? CreateDocumentItem(
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

        var score = MaxScore(
            query,
            new WeightedField(number, 1.00m, 0.97m, 0.94m, 0.88m),
            new WeightedField(display, 0.93m, 0.87m, 0.84m, 0.74m),
            new WeightedField(descriptor.Label, 0.85m, 0.79m, 0.76m, 0.68m),
            new WeightedField(descriptor.Code, 0.88m, 0.82m, 0.78m, 0.70m),
            descriptor.Aliases.Select(alias => new WeightedField(alias, 0.82m, 0.78m, 0.74m, 0.67m)).ToArray());

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

    private CommandPaletteResultItemDto? CreateCatalogItem(
        string query,
        SearchableDescriptor descriptor,
        CatalogLookupDto catalogItem,
        CommandPaletteSearchContextDto? context)
    {
        var display = catalogItem.Display?.Trim();
        var title = display?.Length > 0
            ? display
            : $"{descriptor.Label} {catalogItem.Id}";

        var subtitleParts = new List<string>(capacity: 2)
        {
            descriptor.Label,
        };

        if (catalogItem.IsMarkedForDeletion)
            subtitleParts.Add("Marked for deletion");

        var score = MaxScore(
            query,
            new WeightedField(display, 0.97m, 0.90m, 0.86m, 0.76m),
            new WeightedField(descriptor.Label, 0.84m, 0.80m, 0.76m, 0.68m),
            new WeightedField(descriptor.Code, 0.82m, 0.78m, 0.74m, 0.66m),
            descriptor.Aliases.Select(alias => new WeightedField(alias, 0.80m, 0.76m, 0.72m, 0.65m)).ToArray());

        if (score <= 0m)
            return null;

        if (string.Equals(context?.CatalogType, descriptor.Code, StringComparison.OrdinalIgnoreCase))
            score += 0.03m;

        return new CommandPaletteResultItemDto(
            Key: $"catalog:{descriptor.Code}:{catalogItem.Id}",
            Kind: "catalog",
            Title: title,
            Subtitle: string.Join(" · ", subtitleParts),
            Icon: descriptor.Icon,
            Badge: "Catalog",
            Route: BuildCatalogRoute(descriptor.Code, catalogItem.Id),
            CommandCode: null,
            Status: catalogItem.IsMarkedForDeletion ? "marked-for-deletion" : null,
            OpenInNewTabSupported: true,
            Score: decimal.Round(score, 4));
    }

    private CommandPaletteResultItemDto? CreateReportItem(
        string query,
        ReportDefinitionDto definition,
        CommandPaletteSearchContextDto? context)
    {
        var group = definition.Group?.Trim();
        var description = definition.Description?.Trim();

        var score = MaxScore(
            query,
            new WeightedField(definition.Name, 0.96m, 0.90m, 0.86m, 0.78m),
            new WeightedField(definition.ReportCode, 0.90m, 0.84m, 0.80m, 0.72m),
            new WeightedField(group, 0.72m, 0.68m, 0.64m, 0.58m),
            new WeightedField(description, 0.68m, 0.64m, 0.60m, 0.54m));

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
            Icon: ResolveReportIcon(definition),
            Badge: "Report",
            Route: $"/reports/{definition.ReportCode}",
            CommandCode: null,
            Status: null,
            OpenInNewTabSupported: true,
            Score: decimal.Round(score, 4));
    }

    private async Task<IReadOnlyList<SearchableDescriptor>> GetDocumentDescriptorsAsync(CancellationToken ct)
        => await cache.GetOrCreateAsync(
               "command-palette:documents",
               async entry =>
               {
                   entry.AbsoluteExpirationRelativeToNow = MetadataCacheTtl;

                   var metadata = await documents.GetAllMetadataAsync(ct);
                   return metadata
                       .Where(static item =>
                           item.DocumentType.StartsWith("pm.", StringComparison.OrdinalIgnoreCase)
                           || item.DocumentType.StartsWith("accounting.", StringComparison.OrdinalIgnoreCase))
                       .Select(item => new SearchableDescriptor(
                           item.DocumentType,
                           item.DisplayName,
                           ResolveItemIcon(item.Icon, fallback: "file-text"),
                           ResolveAliases(item.DocumentType, item.DisplayName)))
                       .ToArray();
               })
           ?? [];

    private async Task<IReadOnlyList<SearchableDescriptor>> GetCatalogDescriptorsAsync(CancellationToken ct)
        => await cache.GetOrCreateAsync(
               "command-palette:catalogs",
               async entry =>
               {
                   entry.AbsoluteExpirationRelativeToNow = MetadataCacheTtl;

                   var metadata = await catalogs.GetAllMetadataAsync(ct);
                   return metadata
                       .Where(static item => item.CatalogType.StartsWith("pm.", StringComparison.OrdinalIgnoreCase))
                       .Where(static item => !string.Equals(item.CatalogType, PropertyManagementCodes.AccountingPolicy, StringComparison.OrdinalIgnoreCase))
                       .Select(item => new SearchableDescriptor(
                           item.CatalogType,
                           item.DisplayName,
                           ResolveItemIcon(item.Icon, fallback: "grid"),
                           ResolveAliases(item.CatalogType, item.DisplayName)))
                       .ToArray();
               })
           ?? [];

    private async Task<IReadOnlyList<ReportDefinitionDto>> GetReportDefinitionsAsync(CancellationToken ct)
        => await cache.GetOrCreateAsync(
               "command-palette:reports",
               async entry =>
               {
                   entry.AbsoluteExpirationRelativeToNow = MetadataCacheTtl;

                   var definitions = await reports.GetAllDefinitionsAsync(ct);
                   return definitions
                       .Where(static definition =>
                           definition.ReportCode.StartsWith("pm.", StringComparison.OrdinalIgnoreCase)
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
            _ when string.Equals(code, PropertyManagementCodes.Property, StringComparison.OrdinalIgnoreCase)
                => ["property", "building", "unit", label],
            _ when string.Equals(code, PropertyManagementCodes.Party, StringComparison.OrdinalIgnoreCase)
                => ["party", "tenant", "vendor", label],
            _ when string.Equals(code, PropertyManagementCodes.BankAccount, StringComparison.OrdinalIgnoreCase)
                => ["bank", "bank account", label],
            _ when string.Equals(code, PropertyManagementCodes.ReceivableChargeType, StringComparison.OrdinalIgnoreCase)
                => ["receivable", "charge type", label],
            _ when string.Equals(code, PropertyManagementCodes.PayableChargeType, StringComparison.OrdinalIgnoreCase)
                => ["payable", "charge type", label],
            _ when string.Equals(code, PropertyManagementCodes.MaintenanceCategory, StringComparison.OrdinalIgnoreCase)
                => ["maintenance", "category", label],
            _ when string.Equals(code, PropertyManagementCodes.Lease, StringComparison.OrdinalIgnoreCase)
                => ["lease", "leases", label],
            _ when string.Equals(code, PropertyManagementCodes.MaintenanceRequest, StringComparison.OrdinalIgnoreCase)
                => ["maintenance", "request", label],
            _ when string.Equals(code, PropertyManagementCodes.WorkOrder, StringComparison.OrdinalIgnoreCase)
                => ["work order", label],
            _ when string.Equals(code, PropertyManagementCodes.WorkOrderCompletion, StringComparison.OrdinalIgnoreCase)
                => ["completion", "work order completion", label],
            _ when string.Equals(code, PropertyManagementCodes.RentCharge, StringComparison.OrdinalIgnoreCase)
                => ["rent", "rent charge", label],
            _ when string.Equals(code, PropertyManagementCodes.ReceivableCharge, StringComparison.OrdinalIgnoreCase)
                => ["receivable", "charge", "other charge", "other charges", label],
            _ when string.Equals(code, PropertyManagementCodes.LateFeeCharge, StringComparison.OrdinalIgnoreCase)
                => ["late fee", "late fees", "charge", label],
            _ when string.Equals(code, PropertyManagementCodes.ReceivablePayment, StringComparison.OrdinalIgnoreCase)
                => ["receivable", "payment", "payments", label],
            _ when string.Equals(code, PropertyManagementCodes.ReceivableReturnedPayment, StringComparison.OrdinalIgnoreCase)
                => ["returned payment", label],
            _ when string.Equals(code, PropertyManagementCodes.ReceivableCreditMemo, StringComparison.OrdinalIgnoreCase)
                => ["credit memo", "credit memos", "receivable credit memo", label],
            _ when string.Equals(code, PropertyManagementCodes.ReceivableApply, StringComparison.OrdinalIgnoreCase)
                => ["allocation", "allocations", "apply", "receivable apply", label],
            _ when string.Equals(code, PropertyManagementCodes.PayableCharge, StringComparison.OrdinalIgnoreCase)
                => ["payable", "charge", "charges", label],
            _ when string.Equals(code, PropertyManagementCodes.PayablePayment, StringComparison.OrdinalIgnoreCase)
                => ["payable", "payment", "payments", label],
            _ when string.Equals(code, PropertyManagementCodes.PayableCreditMemo, StringComparison.OrdinalIgnoreCase)
                => ["payable credit memo", "credit memo", "credit memos", label],
            _ when string.Equals(code, PropertyManagementCodes.PayableApply, StringComparison.OrdinalIgnoreCase)
                => ["allocation", "allocations", "payable apply", "apply", label],
            _ => [label, code],
        };

    private static string ResolveItemIcon(string? icon, string fallback)
        => icon?.Trim() switch
        {
            { Length: > 0 } value => value,
            _ => fallback,
        };

    private static string ResolveReportIcon(ReportDefinitionDto definition)
    {
        var reportCode = definition.ReportCode.Trim();

        return reportCode switch
        {
            "pm.tenant.statement" => "file-text",
            "pm.maintenance.queue" => "list",
            "pm.receivables.open_items" => "list",
            "pm.receivables.open_items.details" => "file-text",
            "accounting.general_journal" => "receipt",
            "accounting.account_card" => "book-open",
            "accounting.general_ledger_aggregated" => "book-open",
            _ => "bar-chart",
        };
    }

    private static string BuildCatalogRoute(string catalogType, Guid id)
        => string.Equals(catalogType, PropertyManagementCodes.Property, StringComparison.OrdinalIgnoreCase)
            ? $"/catalogs/{catalogType}?panel=edit&id={id}"
            : $"/catalogs/{catalogType}/{id}";

    private static string GetDocumentStatusLabel(DocumentStatus status)
        => status switch
        {
            DocumentStatus.Draft => "Draft",
            DocumentStatus.Posted => "Posted",
            DocumentStatus.MarkedForDeletion => "Marked for deletion",
            _ => status.ToString(),
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
            _ => 99,
        };

    private static decimal MaxScore(string query, params WeightedField[] fields)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
            return 0m;

        var best = 0m;
        foreach (var field in fields)
        {
            best = Math.Max(best, ScoreField(normalizedQuery, field));
        }

        return best;
    }

    private static decimal MaxScore(
        string query,
        WeightedField primary,
        WeightedField secondary,
        WeightedField tertiary,
        WeightedField[] extras)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
            return 0m;

        var best = MaxScore(query, primary, secondary, tertiary);
        foreach (var extra in extras)
        {
            best = Math.Max(best, ScoreField(normalizedQuery, extra));
        }

        return best;
    }

    private static decimal MaxScore(
        string query,
        WeightedField primary,
        WeightedField secondary,
        WeightedField tertiary,
        WeightedField quaternary,
        WeightedField[] extras)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
            return 0m;

        var best = MaxScore(query, primary, secondary, tertiary, quaternary);
        foreach (var extra in extras)
        {
            best = Math.Max(best, ScoreField(normalizedQuery, extra));
        }

        return best;
    }

    private static decimal ScoreField(string normalizedQuery, WeightedField field)
    {
        var normalizedValue = Normalize(field.Value);
        if (normalizedValue.Length == 0)
            return 0m;

        if (string.Equals(normalizedValue, normalizedQuery, StringComparison.Ordinal))
            return field.Exact;

        if (normalizedValue.StartsWith(normalizedQuery, StringComparison.Ordinal))
            return field.Prefix;

        if (ContainsWordPrefix(normalizedValue, normalizedQuery))
            return field.WordPrefix;

        return normalizedValue.Contains(normalizedQuery, StringComparison.Ordinal)
            ? field.Contains
            : 0m;
    }

    private static bool ContainsWordPrefix(string normalizedValue, string normalizedQuery)
    {
        var remaining = normalizedValue.AsSpan();
        while (remaining.Length > 0)
        {
            var nextSpace = remaining.IndexOf(' ');
            var word = nextSpace >= 0 ? remaining[..nextSpace] : remaining;
            if (word.StartsWith(normalizedQuery, StringComparison.Ordinal))
                return true;

            if (nextSpace < 0)
                break;

            remaining = remaining[(nextSpace + 1)..];
        }

        return false;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Trim();
        var builder = new StringBuilder(text.Length);
        var lastWasSeparator = false;

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSeparator = false;
                continue;
            }

            if (lastWasSeparator)
                continue;

            builder.Append(' ');
            lastWasSeparator = true;
        }

        return builder.ToString().Trim();
    }

    private readonly record struct SearchableDescriptor(
        string Code,
        string Label,
        string Icon,
        string[] Aliases);

    private readonly record struct WeightedField(
        string? Value,
        decimal Exact,
        decimal Prefix,
        decimal WordPrefix,
        decimal Contains);
}
