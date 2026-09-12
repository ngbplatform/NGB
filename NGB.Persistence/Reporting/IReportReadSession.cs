namespace NGB.Persistence.Reporting;

/// <summary>One consistent source view across every query needed to build a report attempt.</summary>
public interface IReportReadSession
{
    Task BeginAsync(CancellationToken ct);
}
