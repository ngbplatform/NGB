using NGB.Contracts.Reporting;

namespace NGB.Application.Abstractions.Services;

/// <summary>
/// Allows vertical modules to enrich report metadata without taking a dependency on platform handlers.
/// Intended for UI/report-filter metadata that is static for a given report code.
/// </summary>
public interface IReportMetadataContributor
{
    ReportTypeMetadataDto Enrich(ReportTypeMetadataDto metadata);
}
