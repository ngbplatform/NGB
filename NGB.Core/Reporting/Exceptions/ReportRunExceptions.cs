using NGB.Tools.Exceptions;

namespace NGB.Core.Reporting.Exceptions;

public sealed class ReportRunNotFoundException() : NgbNotFoundException(
    "This report result is no longer available. Run the report again.", "report.run.not_found");

public sealed class ReportRunNotReadyException(string status) : NgbException(
    status == "Failed" ? "The report could not be completed. Please run it again."
        : status == "Cancelled" ? "Report generation was cancelled." : "The report is still being prepared.",
    "report.run." + status.ToLowerInvariant(), NgbErrorKind.Conflict);
