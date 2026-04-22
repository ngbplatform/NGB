using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Catalog;

public sealed class BackgroundJobCatalog : IBackgroundJobCatalog
{
    public IReadOnlyList<string> All { get; }

    public BackgroundJobCatalog(IEnumerable<IBackgroundJobCatalogContributor> contributors)
    {
        if (contributors is null)
            throw new NgbArgumentRequiredException(nameof(contributors));

        var ordered = new List<string>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var contributor in contributors)
        {
            if (contributor is null)
                continue;

            var contributorName = contributor.GetType().FullName ?? contributor.GetType().Name;
            var jobIds = contributor.GetJobIds();

            foreach (var rawJobId in jobIds)
            {
                if (string.IsNullOrWhiteSpace(rawJobId))
                {
                    throw new NgbConfigurationViolationException(
                        "Background job catalog contains an empty JobId.",
                        new Dictionary<string, object?>
                        {
                            ["contributor"] = contributorName
                        });
                }

                var jobId = rawJobId.Trim();
                if (owners.TryGetValue(jobId, out var existingContributor))
                {
                    throw new NgbConfigurationViolationException(
                        $"Duplicate background job JobId '{jobId}'.",
                        new Dictionary<string, object?>
                        {
                            ["jobId"] = jobId,
                            ["existingContributor"] = existingContributor,
                            ["duplicateContributor"] = contributorName
                        });
                }

                owners[jobId] = contributorName;
                ordered.Add(jobId);
            }
        }

        All = ordered;
    }

    internal static IBackgroundJobCatalog FromJobIds(IEnumerable<string> jobIds)
    {
        if (jobIds is null)
            throw new NgbArgumentRequiredException(nameof(jobIds));

        return new BackgroundJobCatalog([new InlineContributor(jobIds)]);
    }

    private sealed class InlineContributor(IEnumerable<string> jobIds) : IBackgroundJobCatalogContributor
    {
        private readonly IReadOnlyCollection<string> _jobIds = jobIds.ToArray();

        public IReadOnlyCollection<string> GetJobIds() => _jobIds;
    }
}
