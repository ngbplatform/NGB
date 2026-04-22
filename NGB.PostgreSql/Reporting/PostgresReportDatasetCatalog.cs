using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportDatasetCatalog
{
    private readonly Dictionary<string, PostgresReportDatasetBinding> _datasets;

    public PostgresReportDatasetCatalog(IEnumerable<IPostgresReportDatasetSource> sources)
    {
        if (sources is null)
            throw new NgbConfigurationViolationException("PostgreSQL reporting dataset catalog requires dataset sources registration.");

        _datasets = new Dictionary<string, PostgresReportDatasetBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataset in sources.SelectMany(static x => x.GetDatasets() ?? []))
        {
            var codeNorm = dataset.DatasetCodeNorm;
            if (!_datasets.TryAdd(codeNorm, dataset))
                throw new NgbConfigurationViolationException($"Duplicate PostgreSQL reporting dataset binding '{codeNorm}'.");
        }
    }

    public PostgresReportDatasetBinding GetDataset(string datasetCode)
    {
        var codeNorm = CodeNormalizer.NormalizeCodeNorm(datasetCode, nameof(datasetCode));
        if (_datasets.TryGetValue(codeNorm, out var binding))
            return binding;

        throw new NgbConfigurationViolationException($"PostgreSQL reporting dataset binding '{codeNorm}' is not registered.");
    }
}
