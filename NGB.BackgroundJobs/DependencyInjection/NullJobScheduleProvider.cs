using NGB.BackgroundJobs.Contracts;

namespace NGB.BackgroundJobs.DependencyInjection;

internal sealed class NullJobScheduleProvider : IJobScheduleProvider
{
    public JobSchedule? GetSchedule(string jobId) => null;
}
