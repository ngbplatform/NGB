using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed class ReportDefinitionCatalog : IReportDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, ReportDefinitionDto> _definitions;
    private readonly IReadOnlyList<ReportDefinitionDto> _all;

    public ReportDefinitionCatalog(IEnumerable<IReportDefinitionSource> sources)
        : this(sources, [])
    {
    }

    public ReportDefinitionCatalog(IEnumerable<IReportDefinitionSource> sources, IEnumerable<IReportDefinitionEnricher> enrichers)
    {
        var dict = new Dictionary<string, ReportDefinitionDto>(StringComparer.OrdinalIgnoreCase);
        var enrichersList = enrichers?.Where(x => x is not null).ToArray() ?? [];

        foreach (var source in sources)
        {
            if (source is null)
                continue;

            foreach (var definition in source.GetDefinitions())
            {
                var codeNorm = CodeNormalizer.NormalizeCodeNorm(definition.ReportCode, nameof(definition.ReportCode));
                var enriched = definition;
                foreach (var enricher in enrichersList)
                {
                    enriched = enricher.Enrich(enriched);
                    var enrichedCodeNorm = CodeNormalizer.NormalizeCodeNorm(enriched.ReportCode, nameof(enriched.ReportCode));
                    if (!string.Equals(codeNorm, enrichedCodeNorm, StringComparison.OrdinalIgnoreCase))
                        throw new NgbInvariantViolationException($"Report enricher changed report code from '{codeNorm}' to '{enrichedCodeNorm}'.");
                }

                if (!dict.TryAdd(codeNorm, enriched))
                    throw new NgbInvariantViolationException($"Duplicate report code '{codeNorm}'.");
            }
        }

        _definitions = dict;
        _all = dict.Values
            .OrderBy(x => x.Group ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<IReadOnlyList<ReportDefinitionDto>> GetAllDefinitionsAsync(CancellationToken ct) 
        => Task.FromResult(_all);

    public Task<ReportDefinitionDto> GetDefinitionAsync(string reportCode, CancellationToken ct)
    {
        var codeNorm = CodeNormalizer.NormalizeCodeNorm(reportCode, nameof(reportCode));
        if (_definitions.TryGetValue(codeNorm, out var definition))
            return Task.FromResult(definition);

        throw new ReportTypeNotFoundException(reportCode);
    }
}
