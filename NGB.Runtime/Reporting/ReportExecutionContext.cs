using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting;

public sealed record ReportExecutionContext(
    ReportDefinitionRuntimeModel Definition,
    ReportExecutionRequestDto Request,
    ReportLayoutDto EffectiveLayout);
