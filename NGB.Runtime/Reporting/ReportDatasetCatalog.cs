using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed class ReportDatasetCatalog : IReportDatasetCatalog
{
    private readonly IReadOnlyDictionary<string, ReportDatasetDefinition> _datasets;
    private readonly IReadOnlyList<ReportDatasetDto> _all;

    public ReportDatasetCatalog(IEnumerable<IReportDatasetSource> sources)
    {
        if (sources is null)
            throw new NgbConfigurationViolationException("Reporting dataset catalog requires dataset sources enumeration.");

        var datasets = new Dictionary<string, ReportDatasetDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (source is null)
                continue;

            foreach (var datasetDto in source.GetDatasets() ?? [])
            {
                var runtime = new ReportDatasetDefinition(datasetDto);
                if (!datasets.TryAdd(runtime.DatasetCodeNorm, runtime))
                    throw new NgbConfigurationViolationException($"Duplicate report dataset code '{runtime.DatasetCodeNorm}'.");
            }
        }

        _datasets = datasets;
        _all = datasets.Values
            .Select(x => x.Dataset)
            .OrderBy(x => x.DatasetCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<IReadOnlyList<ReportDatasetDto>> GetAllDatasetsAsync(CancellationToken ct) => Task.FromResult(_all);

    public Task<ReportDatasetDto> GetDatasetAsync(string datasetCode, CancellationToken ct)
    {
        var codeNorm = CodeNormalizer.NormalizeCodeNorm(datasetCode, nameof(datasetCode));
        if (_datasets.TryGetValue(codeNorm, out var dataset))
            return Task.FromResult(dataset.Dataset);

        throw new ReportDatasetTypeNotFoundException(datasetCode);
    }
}
