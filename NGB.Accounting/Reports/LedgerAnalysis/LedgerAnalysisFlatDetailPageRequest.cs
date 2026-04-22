using System.Text.Json;

namespace NGB.Accounting.Reports.LedgerAnalysis;

public sealed record LedgerAnalysisFlatDetailPageRequest(
    string DatasetCode,
    IReadOnlyList<LedgerAnalysisFlatDetailFieldSelection> DetailFields,
    IReadOnlyList<LedgerAnalysisFlatDetailMeasureSelection> Measures,
    IReadOnlyList<LedgerAnalysisFlatDetailPredicate> Predicates,
    DateTime FromUtc,
    DateTime ToUtcExclusive,
    int PageSize,
    LedgerAnalysisFlatDetailCursor? Cursor = null,
    bool DisablePaging = false);

public sealed record LedgerAnalysisFlatDetailFieldSelection(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType);

public sealed record LedgerAnalysisFlatDetailMeasureSelection(
    string MeasureCode,
    string OutputCode,
    string Label,
    string DataType);

public sealed record LedgerAnalysisFlatDetailPredicate(
    string FieldCode,
    string OutputCode,
    string Label,
    string DataType,
    JsonElement Value);
