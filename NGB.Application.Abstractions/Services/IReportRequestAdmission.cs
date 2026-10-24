namespace NGB.Application.Abstractions.Services;

/// <summary>Shared per-host admission for interactive report requests from every transport.</summary>
public interface IReportRequestAdmission
{
    TimeSpan ExecutionTimeout { get; }
    ValueTask<IDisposable?> AcquireAsync(CancellationToken ct);
}
